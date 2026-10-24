using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Booking;
using Office.Application.Common;
using Office.Application.Conversations;
using Office.Application.Ports;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.UseCases;
using Office.Domain.Booking;
using Office.Domain.Tasks;
using Office.Infrastructure.Json;
using Office.Infrastructure.Panel;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Booking;

// TASK 08 (DECISIONS v1.12, B-24): the booking from the panel. The default hours of the week, the
// exceptions of a date, the booking settings, the office's details (P1), «undo», the appointments and
// a preview of what Hazem would offer. Every change is in hazem.booking_changes and hazem.panel_audit.
// A change that leaves confirmed appointments outside the hours is refused until the person confirms;
// then those appointments are cancelled by the office and each client is told (B-22: another time
// or a full refund).

public sealed record RangeView(int Start, int End);

public sealed record WeekDayView(int Weekday, IReadOnlyList<RangeView> Ranges);

public sealed record BookingSettingsView(bool Enabled, int BufferMinutes, int MinNoticeHours, int HorizonDays, int? MaxPerDay, int SlotStepMinutes,
    int OfferCount, string CalendarId, bool CalendarKey, bool TextsApproved, int HoldMinutes = 120, int HoldShortMinutes = 60,
    int HoldShortWithinHours = 6, int HoldGapMinutes = 15, int UrfMinutes = 60);

public sealed record ExceptionView(Guid Id, DateOnly Day, string Kind, int? Start, int? End, string? Reason, string? By, DateTimeOffset At);

public sealed record AppointmentView(Guid Id, string Service, DateTimeOffset Start, DateTimeOffset End, string State, string? Name, string WaId,
    string? MatterType, int Reschedules, string Calendar, DateTimeOffset? HoldUntil = null, string? Payment = null);

public sealed record DayView(DateOnly Day, string Status, IReadOnlyList<RangeView> Hours, int Bookings, IReadOnlyList<ExceptionView> Exceptions);

public sealed record ChangeView(long Id, DateTimeOffset At, string What, string? By, bool Undone);

public sealed record PreviewView(string Calendar, IReadOnlyList<DateTimeOffset> Slots);

public sealed record OfficeView(string Instapay, string WalletNumber, string WalletProvider, string Address, string MapUrl);

/// <summary>
/// What a change did: Error null = done (<see cref="Cancelled"/> appointments cancelled by the office;
/// the worker tells their clients); otherwise the error, or the appointments it would cancel.
/// </summary>
public sealed record ChangeResult(string? Error, IReadOnlyList<AppointmentView>? Affected = null, int Cancelled = 0)
{
    public static ChangeResult Done(int cancelled = 0) => new(null, null, cancelled);
}

public sealed class PanelBooking(
    HazemDbContext db,
    BookingStore store,
    IBusyCalendar calendar,
    GoogleCalendar google,
    ISettingsReader settingsReader,
    IApprovedTexts texts,
    TimeProvider clock)
{
    public const string ErrInvalid = "invalid";
    public const string ErrPast = "past_day";
    public const string ErrNotFound = "not_found";
    public const string ErrNothingToUndo = "nothing_to_undo";
    public const string ErrHasBookings = "has_bookings";

    private static TimeZoneInfo Zone => BookingDesk.Zone;

    // ---------- the week ----------

    public async Task<IReadOnlyList<WeekDayView>> WeekAsync(CancellationToken ct)
    {
        var rows = await db.BookingWeeklyHours.AsNoTracking().ToListAsync(ct);
        return Enumerable.Range(0, 7).Select(d => new WeekDayView(d,
            rows.Where(r => r.Weekday == d).OrderBy(r => r.StartMinute).Select(r => new RangeView(r.StartMinute, r.EndMinute)).ToList())).ToList();
    }

    public async Task<ChangeResult> SaveWeekAsync(Guid by, IReadOnlyList<WeekDayView>? days, bool confirm, CancellationToken ct)
    {
        if (days is null || days.Any(d => d.Weekday is < 0 or > 6) || days.Select(d => d.Weekday).Distinct().Count() != days.Count)
        {
            return new ChangeResult(ErrInvalid);
        }

        var (weekly, problem) = WeeklyHours.Create(days.SelectMany(d => (d.Ranges ?? []).Select(r => ((DayOfWeek)d.Weekday, new MinuteRange(r.Start, r.End)))));
        if (weekly is null)
        {
            return new ChangeResult(problem);
        }

        var before = await WeekAsync(ct);
        return await ChangeHoursAsync(by, confirm, async () =>
        {
            await WriteWeekAsync(days, ct);
            await ChangeAsync(by, "weekly", before, days, ct);
            await PanelAudit.WriteAsync(db, clock, by, "booking.weekly", "booking", null, new Dictionary<string, string?>(), ct);
        }, ct);
    }

    private async Task WriteWeekAsync(IEnumerable<WeekDayView> days, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM hazem.booking_weekly_hours", ct);
        foreach (var d in days)
        {
            foreach (var r in d.Ranges ?? [])
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO hazem.booking_weekly_hours (weekday, start_minute, end_minute) VALUES ({(short)d.Weekday}, {r.Start}, {r.End})", ct);
            }
        }
    }

    // ---------- the exceptions of a date ----------

    public async Task<IReadOnlyList<ExceptionView>> ExceptionsAsync(DateOnly first, DateOnly last, CancellationToken ct) =>
        await (from e in db.BookingExceptions.AsNoTracking()
               join u in db.PanelUsers.AsNoTracking() on e.CreatedBy equals (Guid?)u.Id into users
               from u in users.DefaultIfEmpty()
               where e.Day >= first && e.Day <= last
               orderby e.Day, e.StartMinute
               select new ExceptionView(e.Id, e.Day, e.Kind, e.StartMinute, e.EndMinute, e.Reason, u == null ? null : u.DisplayName, e.CreatedAt))
            .ToListAsync(ct);

    public async Task<ChangeResult> AddExceptionAsync(Guid by, DateOnly day, string? kind, int? start, int? end, string? reason, bool confirm,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (kind is not ("closed_day" or "closed_hours" or "custom_hours") || reason?.Length > 200 || day > BookingDesk.Today(now).AddYears(2))
        {
            return new ChangeResult(ErrInvalid);
        }

        if (day < BookingDesk.Today(now))
        {
            return new ChangeResult(ErrPast);
        }

        var range = kind == "closed_day" ? (MinuteRange?)null : new MinuteRange(start ?? -1, end ?? -1);
        if (range is { IsValid: false })
        {
            return new ChangeResult(ErrInvalid);
        }

        var id = Guid.NewGuid();
        return await ChangeHoursAsync(by, confirm, async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.booking_exceptions (id, day, kind, start_minute, end_minute, reason, created_by)
                VALUES ({id}, {day}, {kind}, {range?.Start}, {range?.End}, {reason}, {by})
                """, ct);
            await ChangeAsync(by, "exception_add", null, new { id, day, kind, start = range?.Start, end = range?.End, reason }, ct);
            await PanelAudit.WriteAsync(db, clock, by, "booking.exception_add", "booking", id,
                new Dictionary<string, string?> { ["day"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["kind"] = kind }, ct);
        }, ct);
    }

    /// <summary>Removing «other hours that day» can leave bookings outside the week's hours: the same check.</summary>
    public async Task<ChangeResult> RemoveExceptionAsync(Guid by, Guid id, bool confirm, CancellationToken ct)
    {
        var row = await db.BookingExceptions.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null)
        {
            return new ChangeResult(ErrNotFound);
        }

        return await ChangeHoursAsync(by, confirm, async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM hazem.booking_exceptions WHERE id = {id}", ct);
            await ChangeAsync(by, "exception_remove",
                new { id, day = row.Day, kind = row.Kind, start = row.StartMinute, end = row.EndMinute, reason = row.Reason }, null, ct);
            await PanelAudit.WriteAsync(db, clock, by, "booking.exception_remove", "booking", id,
                new Dictionary<string, string?> { ["day"] = row.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, ct);
        }, ct);
    }

    // ---------- the settings and the office's details ----------

    public async Task<BookingSettingsView> SettingsAsync(CancellationToken ct)
    {
        var s = await settingsReader.GetAsync(ct);
        var b = BookingSettings.From(s);
        var active = await texts.GetActiveAsync(ct);
        return new BookingSettingsView(b.Enabled, b.Rules.BufferMinutes, b.Rules.MinNoticeHours, b.Rules.HorizonDays, b.Rules.MaxPerDay,
            b.Rules.SlotStepMinutes, b.Rules.OfferCount, b.CalendarId, google.HasKey,
            HazemResponder.BookingTextKeys.All(k => active.ContainsKey(k)), b.Hold.Minutes, b.Hold.ShortMinutes, b.Hold.ShortWithinHours,
            b.Hold.GapMinutes, b.UrfMinutes);
    }

    public async Task<string?> SaveSettingsAsync(Guid by, string username, BookingSettingsView body, CancellationToken ct)
    {
        var rules = new BookingRules(body.BufferMinutes, body.MinNoticeHours, body.HorizonDays, body.MaxPerDay, body.SlotStepMinutes, body.OfferCount);
        var calendarId = (body.CalendarId ?? "").Trim();
        var hold = new HoldRules(body.HoldMinutes, body.HoldShortMinutes, body.HoldShortWithinHours, body.HoldGapMinutes);
        if ((rules.Problem() ?? hold.Problem() ?? (BookingSettings.UrfMinutesValid(body.UrfMinutes) ? null : "urf_minutes")) is { } p)
        {
            return p;
        }

        if (calendarId.Length > 200 || calendarId.Any(char.IsWhiteSpace))
        {
            return "calendar_id";
        }

        var values = new Dictionary<string, string>
        {
            [BookingSettings.EnabledKey] = body.Enabled ? "true" : "false",
            [BookingSettings.BufferKey] = rules.BufferMinutes.ToString(CultureInfo.InvariantCulture),
            [BookingSettings.MinNoticeKey] = rules.MinNoticeHours.ToString(CultureInfo.InvariantCulture),
            [BookingSettings.HorizonKey] = rules.HorizonDays.ToString(CultureInfo.InvariantCulture),
            [BookingSettings.MaxPerDayKey] = rules.MaxPerDay?.ToString(CultureInfo.InvariantCulture) ?? "",
            [BookingSettings.StepKey] = rules.SlotStepMinutes.ToString(CultureInfo.InvariantCulture),
            [BookingSettings.OfferCountKey] = rules.OfferCount.ToString(CultureInfo.InvariantCulture),
            [BookingSettings.CalendarIdKey] = calendarId,
            [BookingSettings.HoldMinutesKey] = hold.Minutes.ToString(CultureInfo.InvariantCulture),
            [BookingSettings.HoldShortMinutesKey] = hold.ShortMinutes.ToString(CultureInfo.InvariantCulture),
            [BookingSettings.HoldShortWithinKey] = hold.ShortWithinHours.ToString(CultureInfo.InvariantCulture),
            [BookingSettings.HoldGapKey] = hold.GapMinutes.ToString(CultureInfo.InvariantCulture),
            [BookingSettings.UrfMinutesKey] = body.UrfMinutes.ToString(CultureInfo.InvariantCulture),
        };
        await SetAsync(by, username, "settings", "booking.settings", values, ct);
        return null;
    }

    public async Task<OfficeView> OfficeAsync(CancellationToken ct)
    {
        var o = OfficeInfo.From(await settingsReader.GetAsync(ct));
        return new OfficeView(o.InstaPay, o.WalletNumber, o.WalletProvider, o.Address, o.MapUrl);
    }

    public async Task<string?> SaveOfficeAsync(Guid by, string username, OfficeView body, CancellationToken ct)
    {
        var values = new Dictionary<string, string>
        {
            [OfficeInfo.InstaPayKey] = (body.Instapay ?? "").Trim(),
            [OfficeInfo.WalletNumberKey] = (body.WalletNumber ?? "").Trim(),
            [OfficeInfo.WalletProviderKey] = (body.WalletProvider ?? "").Trim(),
            [OfficeInfo.AddressKey] = (body.Address ?? "").Trim(),
            [OfficeInfo.MapUrlKey] = (body.MapUrl ?? "").Trim(),
        };
        // The details go into Hazem's messages: plain values within B-21, no slots, a real map link.
        if (values.Values.Any(v => v.Length > 200 || TextRenderer.HasOpenSlot(v))
            || values[OfficeInfo.MapUrlKey] is { Length: > 0 } map && !(Uri.TryCreate(map, UriKind.Absolute, out var u) && u.Scheme == "https"))
        {
            return ErrInvalid;
        }

        await SetAsync(by, username, "office", "booking.office", values, ct);
        return null;
    }

    private async Task SetAsync(Guid by, string username, string what, string action, Dictionary<string, string> values, CancellationToken ct)
    {
        var before = await db.Settings.AsNoTracking().Where(s => values.Keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var (key, value) in values)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.settings SET value = {value}, updated_by = {"panel:" + username} WHERE key = {key}", ct);
        }

        await ChangeAsync(by, what, before, values, ct);
        await PanelAudit.WriteAsync(db, clock, by, action, "booking", null, new Dictionary<string, string?>(), ct);
        await tx.CommitAsync(ct);
    }

    // ---------- undo and the record ----------

    public async Task<IReadOnlyList<ChangeView>> ChangesAsync(CancellationToken ct) =>
        await (from c in db.BookingChanges.AsNoTracking()
               join u in db.PanelUsers.AsNoTracking() on c.UserId equals (Guid?)u.Id into users
               from u in users.DefaultIfEmpty()
               orderby c.Id descending
               select new ChangeView(c.Id, c.At, c.What, u == null ? null : u.DisplayName, c.UndoneAt != null)).Take(30).ToListAsync(ct);

    /// <summary>
    /// The last change not undone yet, put back as it was. Hours that would leave confirmed bookings
    /// outside are checked like any other change (asks first; appointments already cancelled stay cancelled).
    /// </summary>
    public async Task<ChangeResult> UndoAsync(Guid by, string username, bool confirm, CancellationToken ct)
    {
        var last = await db.BookingChanges.AsNoTracking().Where(c => c.UndoneAt == null).OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct);
        if (last is null)
        {
            return new ChangeResult(ErrNothingToUndo);
        }

        return await ChangeHoursAsync(by, confirm, () => UndoChangeAsync(by, username, last, ct), ct);
    }

    private async Task UndoChangeAsync(Guid by, string username, BookingChangeRow last, CancellationToken ct)
    {
        switch (last.What)
        {
            case "weekly":
                await WriteWeekAsync(JsonSerializer.Deserialize<List<WeekDayView>>(last.Before ?? "[]", HazemJson.Options) ?? [], ct);
                break;
            case "exception_add":
                var added = JsonSerializer.Deserialize<ExceptionJson>(last.After ?? "{}", HazemJson.Options);
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM hazem.booking_exceptions WHERE id = {added!.Id}", ct);
                break;
            case "exception_remove":
                var removed = JsonSerializer.Deserialize<ExceptionJson>(last.Before ?? "{}", HazemJson.Options)!;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO hazem.booking_exceptions (id, day, kind, start_minute, end_minute, reason, created_by)
                    VALUES ({removed.Id}, {removed.Day}, {removed.Kind}, {removed.Start}, {removed.End}, {removed.Reason}, {by})
                    ON CONFLICT (id) DO NOTHING
                    """, ct);
                break;
            default: // settings, office
                foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, string>>(last.Before ?? "{}", HazemJson.Options) ?? [])
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE hazem.settings SET value = {value}, updated_by = {"panel:" + username} WHERE key = {key}", ct);
                }

                break;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.booking_changes SET undone_at = now() WHERE id = {last.Id}", ct);
        await PanelAudit.WriteAsync(db, clock, by, "booking.undo", "booking", null,
            new Dictionary<string, string?> { ["what"] = last.What }, ct);
    }

    private sealed record ExceptionJson(Guid Id, DateOnly Day, string Kind, int? Start, int? End, string? Reason);

    private Task ChangeAsync(Guid by, string what, object? before, object? after, CancellationToken ct)
    {
        var b = before is null ? null : JsonSerializer.Serialize(before, HazemJson.Options);
        var a = after is null ? null : JsonSerializer.Serialize(after, HazemJson.Options);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.booking_changes (user_id, what, before, after) VALUES ({by}, {what}, {b}::jsonb, {a}::jsonb)
            """, ct);
    }

    // ---------- the month, the appointments, the preview ----------

    public async Task<IReadOnlyList<DayView>> MonthAsync(int year, int month, CancellationToken ct)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var schedule = await store.LoadScheduleAsync(first, last, ct);
        var exceptions = await ExceptionsAsync(first, last, ct);
        var appointments = await AppointmentsAsync(first, last, ct);
        var days = new List<DayView>();
        for (var d = first; d <= last; d = d.AddDays(1))
        {
            var day = d;
            days.Add(new DayView(day, Status(schedule.StatusOn(day)), schedule.HoursOn(day).Select(r => new RangeView(r.Start, r.End)).ToList(),
                appointments.Count(a => a.State is (AppointmentInfo.Confirmed or AppointmentInfo.PendingPayment) && BookingDesk.Today(a.Start) == day),
                exceptions.Where(e => e.Day == day).ToList()));
        }

        return days;
    }

    private static string Status(DayStatus s) => s switch
    {
        DayStatus.Closed => "closed",
        DayStatus.WeeklyOff => "weekly_off",
        DayStatus.CustomHours => "custom_hours",
        DayStatus.PartlyClosed => "partly_closed",
        _ => "open",
    };

    public async Task<IReadOnlyList<AppointmentView>> AppointmentsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (f, t) = (BookingDesk.Instant(from).ToUniversalTime(), BookingDesk.Instant(to.AddDays(1)).ToUniversalTime());
        return await (from a in db.Appointments.AsNoTracking()
                      join p in db.Persons.AsNoTracking() on a.PersonId equals p.Id
                      join pay in db.Payments.AsNoTracking() on (Guid?)a.Id equals pay.AppointmentId into pays
                      from pay in pays.DefaultIfEmpty()
                      where a.StartsAt >= f && a.StartsAt < t
                      orderby a.StartsAt
                      select new AppointmentView(a.Id, a.Service, a.StartsAt, a.EndsAt, a.State, p.NameRaw, p.WaId, a.MatterType, a.Reschedules,
                          a.CalendarState, a.HoldUntil, pay == null ? null : pay.State)).Take(500).ToListAsync(ct);
    }

    /// <summary>S-03 from the panel: completed or no_show (the lawyer or the staff).</summary>
    public async Task<string?> MarkAsync(Guid by, Guid id, string? state, CancellationToken ct)
    {
        if (state is not (AppointmentInfo.Completed or AppointmentInfo.NoShow))
        {
            return ErrInvalid;
        }

        var changed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE hazem.appointments SET state = {state} WHERE id = {id} AND state = 'confirmed'", ct);
        if (changed == 0)
        {
            return await db.Appointments.AnyAsync(a => a.Id == id, ct) ? "not_confirmed" : ErrNotFound;
        }

        await PanelAudit.WriteAsync(db, clock, by, "appointment." + state, "appointment", id, new Dictionary<string, string?>(), ct);
        return null;
    }

    /// <summary>B-22: the office cancels one appointment; the client chooses another time or a refund.</summary>
    public async Task<ChangeResult> CancelByOfficeAsync(Guid by, Guid id, CancellationToken ct)
    {
        var a = await AppointmentsByIdAsync([id], ct);
        if (a.Count == 0)
        {
            return new ChangeResult(ErrNotFound);
        }

        if (a[0].State is not (AppointmentInfo.Confirmed or AppointmentInfo.PendingPayment))
        {
            return new ChangeResult("not_confirmed");
        }

        return ChangeResult.Done(await CancelForOfficeAsync(by, a, ct));
    }

    public async Task<PreviewView> PreviewAsync(DateOnly day, string? serviceCode, CancellationToken ct)
    {
        var settings = await settingsReader.GetAsync(ct);
        var b = BookingSettings.From(settings);
        var service = b.Fit(BookingService.ByCode(serviceCode) ?? BookingService.PhoneVideo);
        var now = clock.GetUtcNow();
        var from = BookingDesk.Instant(day);
        var to = BookingDesk.Instant(day.AddDays(1));
        var busy = await calendar.BusyAsync(from, to, settings, ct);
        var schedule = await store.LoadScheduleAsync(day, day, ct);
        var booked = (await store.TakenBetweenAsync(from, to, ct)).Select(x => x.Interval).ToList();
        // Without the calendar Hazem offers nothing (B-24); the preview still shows the panel's hours alone, and says so.
        var slots = Availability.Find(schedule, b.Rules, new SlotQuery(service.Minutes, now, service.IsUrgent, day, 96), booked,
            busy.Available ? busy.Busy : [], now, Zone);
        var state = busy.Available ? "ok" : busy.Problem == "not_configured" ? "not_configured" : "unavailable";
        return new PreviewView(state, slots.Select(s => s.Start).ToList());
    }

    // ---------- appointments outside the new hours ----------

    /// <summary>
    /// Every change of the hours (the week, an exception, undo): made in one transaction, then the
    /// confirmed appointments it leaves outside the hours are found from the database as it now is
    /// (the week and the exceptions of every day concerned). Any → rolled back and listed, unless the
    /// person confirmed; then they are cancelled by the office in the same transaction.
    /// </summary>
    private async Task<ChangeResult> ChangeHoursAsync(Guid by, bool confirm, Func<Task> change, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await change();
        var affected = await OutsideHoursAsync(clock.GetUtcNow(), ct);
        if (affected.Count > 0 && !confirm)
        {
            await tx.RollbackAsync(ct);
            return new ChangeResult(ErrHasBookings, affected);
        }

        var cancelled = await CancelForOfficeAsync(by, affected, ct);
        await tx.CommitAsync(ct);
        return ChangeResult.Done(cancelled);
    }

    /// <summary>Confirmed appointments from now on outside the hours of their day.</summary>
    private async Task<IReadOnlyList<AppointmentView>> OutsideHoursAsync(DateTimeOffset now, CancellationToken ct)
    {
        var confirmed = await store.TakenBetweenAsync(now, now.AddYears(2), ct);
        if (confirmed.Count == 0)
        {
            return [];
        }

        var days = confirmed.Select(a => BookingDesk.Today(a.Start)).ToList();
        var schedule = await store.LoadScheduleAsync(days.Min(), days.Max(), ct);
        var outside = confirmed.Where(a =>
        {
            var local = TimeZoneInfo.ConvertTime(a.Start, Zone);
            var startMinute = local.Hour * 60 + local.Minute;
            var range = new MinuteRange(startMinute, startMinute + (int)(a.End - a.Start).TotalMinutes);
            return !schedule.HoursOn(DateOnly.FromDateTime(local.DateTime)).Any(h => h.Start <= range.Start && range.End <= h.End);
        }).Select(a => a.Id).ToList();
        return await AppointmentsByIdAsync(outside, ct);
    }

    private async Task<List<AppointmentView>> AppointmentsByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await (from a in db.Appointments.AsNoTracking()
                     join p in db.Persons.AsNoTracking() on a.PersonId equals p.Id
                     join pay in db.Payments.AsNoTracking() on (Guid?)a.Id equals pay.AppointmentId into pays
                     from pay in pays.DefaultIfEmpty()
                     where ids.Contains(a.Id)
                     orderby a.StartsAt
                     select new AppointmentView(a.Id, a.Service, a.StartsAt, a.EndsAt, a.State, p.NameRaw, p.WaId, a.MatterType, a.Reschedules,
                         a.CalendarState, a.HoldUntil, pay == null ? null : pay.State)).ToListAsync(ct);

    /// <summary>
    /// cancelled_by_office; the client is told by <see cref="BookingNotices"/> (the worker, B-22), never
    /// inside the panel's request: a slow WhatsApp can never leave half the clients cancelled and told.
    /// </summary>
    private async Task<int> CancelForOfficeAsync(Guid by, IReadOnlyList<AppointmentView> appointments, CancellationToken ct)
    {
        var cancelled = 0;
        foreach (var a in appointments)
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE hazem.appointments
                   SET state = 'cancelled_by_office', client_notice = 'pending', notice_kind = 'office_cancelled',
                       calendar_state = CASE WHEN calendar_event_id IS NULL THEN 'removed' ELSE 'remove' END,
                       calendar_attempts = 0, calendar_next_at = now()
                 WHERE id = {a.Id} AND state IN ('confirmed', 'pending_payment')
                """, ct);
            if (changed == 1)
            {
                cancelled++;
                await PanelAudit.WriteAsync(db, clock, by, "appointment.cancelled_by_office", "appointment", a.Id, new Dictionary<string, string?>(), ct);
            }
        }

        return cancelled;
    }
}
