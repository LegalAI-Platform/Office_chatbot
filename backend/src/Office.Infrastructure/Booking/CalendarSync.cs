using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Ports;
using Office.Domain.Tasks;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Booking;

/// <summary>
/// The appointments in the lawyer's Google Calendar (T-14, B-24): made, moved and removed after the
/// booking is saved, so a booking is never lost when Google is slow. A failure waits longer each time
/// (1, 2, 4 … 60 minutes). The event holds what B-24 allows: the service, the name, the number, the
/// kind of matter and the receipt (never the client's words).
/// P3: an event the lawyer adds over a booking makes a task (calendar_conflict), once.
/// </summary>
public sealed class CalendarSync(HazemDbContext db, GoogleCalendar google, ISettingsReader settingsReader, BookingStore store, TimeProvider clock)
{
    public const int Batch = 20;

    public async Task<int> SyncAsync(CancellationToken ct)
    {
        var settings = await settingsReader.GetAsync(ct);
        if (!google.IsConfigured(settings))
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var due = await (from a in db.Appointments.AsNoTracking()
                         join p in db.Persons.AsNoTracking() on a.PersonId equals p.Id
                         join pay in db.Payments.AsNoTracking() on (Guid?)a.Id equals pay.AppointmentId into pays
                         from pay in pays.DefaultIfEmpty()
                         where (a.CalendarState == "create" || a.CalendarState == "update" || a.CalendarState == "remove") && a.CalendarNextAt <= now
                         orderby a.CalendarNextAt
                         select new { a, p.NameRaw, p.WaId, Payment = pay == null ? null : pay.State }).Take(Batch).ToListAsync(ct);
        var done = 0;
        foreach (var x in due)
        {
            var a = x.a;
            var summary = Summary(a, x.NameRaw, x.WaId, x.Payment);
            // ok = Google did what was asked. The state moves only if nothing changed it meanwhile (Mark).
            bool ok;
            switch (a.CalendarState)
            {
                case "create":
                    ok = await CreateAsync(settings, a, summary, ct);
                    break;
                case "update" when a.CalendarEventId is { } ev:
                    ok = await google.UpdateEventAsync(settings, ev, summary, a.StartsAt, a.EndsAt, a.Id, ct);
                    if (ok)
                    {
                        // Only if the row did not change meanwhile (TASK 12: «هحضر» pressed during the update): else once more.
                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                            UPDATE hazem.appointments SET calendar_state = 'synced', calendar_attempts = 0
                             WHERE id = {a.Id} AND calendar_state = 'update' AND updated_at = {a.UpdatedAt.ToUniversalTime()}
                            """, ct);
                    }

                    break;
                case "remove" when a.CalendarEventId is { } ev:
                    ok = await google.DeleteEventAsync(settings, ev, ct);
                    if (ok)
                    {
                        await Mark(a.Id, "remove", "removed", ct);
                    }

                    break;
                default: // no event yet: nothing to change or remove in Google
                    await Mark(a.Id, a.CalendarState, a.CalendarState == "remove" ? "removed" : "create", ct);
                    ok = true;
                    break;
            }

            if (ok)
            {
                done++;
            }
            else
            {
                var wait = Math.Min(Math.Pow(2, Math.Min(a.CalendarAttempts, 6)), 60);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE hazem.appointments SET calendar_attempts = calendar_attempts + 1,
                           calendar_next_at = now() + make_interval(mins => {(int)wait})
                     WHERE id = {a.Id} AND calendar_state = {a.CalendarState}
                    """, ct);
            }
        }

        return done;
    }

    /// <summary>A cancellation or a move while the event was being made is not lost: it becomes «remove» / «update».</summary>
    private async Task<bool> CreateAsync(Application.Responders.HazemSettings settings, AppointmentRow a, string summary, CancellationToken ct)
    {
        if (a.State != AppointmentInfo.Confirmed)
        {
            await Mark(a.Id, "create", "removed", ct);
            return true;
        }

        if (await google.CreateEventAsync(settings, summary, a.StartsAt, a.EndsAt, a.Id, ct) is not { } eventId)
        {
            return false;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.appointments
               SET calendar_event_id = {eventId}, calendar_attempts = 0,
                   calendar_state = CASE WHEN state = 'confirmed' AND calendar_state = 'create' AND starts_at = {a.StartsAt.ToUniversalTime()}
                                         THEN 'synced'
                                         WHEN state = 'confirmed' THEN 'update' ELSE 'remove' END
             WHERE id = {a.Id}
            """, ct);
        return true;
    }

    /// <summary>from → to, only if the state is still <paramref name="from"/> (a later cancel or move wins).</summary>
    private Task Mark(Guid id, string from, string to, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.appointments SET calendar_state = {to}, calendar_attempts = 0 WHERE id = {id} AND calendar_state = {from}
            """, ct);

    /// <summary>P3: a busy event of the lawyer over a confirmed booking → one task for the office.</summary>
    public async Task<int> CheckConflictsAsync(CancellationToken ct)
    {
        var settings = await settingsReader.GetAsync(ct);
        if (!google.IsConfigured(settings))
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var rules = BookingSettings.From(settings).Rules;
        var until = now.AddDays(rules.HorizonDays + 1);
        var booked = await db.Appointments.AsNoTracking()
            .Where(a => a.State == AppointmentInfo.Confirmed && a.CalendarState == "synced" && a.ConflictNotedAt == null
                        && a.StartsAt > now && a.StartsAt < until)
            .ToListAsync(ct);
        if (booked.Count == 0 || await google.ListEventsAsync(settings, now, until, ct) is not { } events)
        {
            return 0;
        }

        var found = 0;
        foreach (var a in booked)
        {
            var clash = events.Any(e => e.AppointmentId is null && e.Id != a.CalendarEventId && e.Start < a.EndsAt && a.StartsAt < e.End);
            if (!clash)
            {
                continue;
            }

            await store.AddTaskAsync(a.PersonId, $"booking:{a.Id:N}:conflict:{a.StartsAt.ToUnixTimeSeconds()}", PendingTask.CalendarConflict,
                OfficeHours.DueAt(now), ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.appointments SET conflict_noted_at = now() WHERE id = {a.Id}", ct);
            found++;
        }

        return found;
    }

    /// <summary>
    /// B-24: «[الخدمة] – [الاسم] – [الرقم] – [نوع الموضوع] – الدفع: …». P6 (TASK 09): a confirmed booking always
    /// has its receipt (1.13), so the event shows the office's review instead, and is updated with it.
    /// </summary>
    public static string Summary(AppointmentRow a, string? name, string waId, string? payment) =>
        $"{BookingService.ByCode(a.Service)?.Name ?? a.Service}{(a.CallApp is { } app ? $" ({CallApp.Name(app)})" : "")} – {name ?? "من غير اسم"} – +{waId} – {MatterName(a.MatterType)} – {PaymentWords(payment)}"
        + (a.AttendanceFor is { } at && at == a.StartsAt ? " – الحضور: ✅ أكّد" : ""); // TASK 12 (B-18): «هحضر ✅»

    public static string PaymentWords(string? state) => state switch
    {
        PaymentInfo.ProofReceived => "الدفع: مستني مراجعتك",
        PaymentInfo.DuplicateSuspected => "الدفع: مستني مراجعتك (صورة مكررة ⚠️)",
        PaymentInfo.Verified => "الدفع: ✅ اتأكد",
        PaymentInfo.Partial => "الدفع: ⚠️ ناقص",
        PaymentInfo.NotReceived => "الدفع: ❌ ما وصلش",
        PaymentInfo.RefundRequested or PaymentInfo.Refunded => "الدفع: استرداد",
        _ => "الدفع: لسه ❌",
    };

    /// <summary>B-25: the kind only, never the client's words.</summary>
    public static string MatterName(string? code) => code switch
    {
        "personal_status" => "أحوال شخصية",
        "inheritance" => "ميراث",
        "rent" => "إيجارات",
        "real_estate" => "عقارات",
        "criminal" => "جنائي",
        "cheques_trust_receipts" => "شيكات وإيصالات أمانة",
        "labor" => "عمالي",
        "commercial" => "تجاري",
        "civil_compensation" => "مدني وتعويضات",
        "urfi" => "عرفي",
        _ => "غير معروف",
    };
}

/// <summary>Every 10 seconds the office's cancellation notices and the calendar events, every 5 minutes the conflicts. Off in tests (Booking:RunWorker=false).</summary>
public sealed class CalendarWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<CalendarWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Booking:RunWorker", true))
        {
            return;
        }

        var round = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var told = await scope.ServiceProvider.GetRequiredService<BookingNotices>().RunAsync(stop)
                           + await scope.ServiceProvider.GetRequiredService<Services.ServiceNotices>().RunAsync(stop) // TASK 10
                           + await scope.ServiceProvider.GetRequiredService<Urf.UrfNotices>().RunAsync(stop) // TASK 11
                           + await scope.ServiceProvider.GetRequiredService<ReminderNotices>().RunAsync(stop) // TASK 12
                           + await scope.ServiceProvider.GetRequiredService<Ratings.RatingNotices>().RunAsync(stop); // v1.31 (B-26)
                var sync = scope.ServiceProvider.GetRequiredService<CalendarSync>();
                var synced = await sync.SyncAsync(stop);
                var conflicts = round++ % 30 == 0 ? await sync.CheckConflictsAsync(stop) : 0;
                if (told + synced + conflicts > 0)
                {
                    log.LogInformation("Booking: {Told} client(s) told, {Synced} event(s) synced, {Conflicts} conflict(s) found", told, synced, conflicts);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Booking round failed: {Kind}: {Message}", e.GetType().Name, e.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
