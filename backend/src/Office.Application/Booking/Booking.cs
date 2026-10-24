using Office.Application.Responders;
using Office.Domain.Booking;
using Office.Domain.Tasks;

namespace Office.Application.Booking;

// TASK 08 (DECISIONS v1.12): booking from WhatsApp. The hours and the rules come from the panel
// (hazem.booking_* and the booking.* settings), the busy times from the lawyer's Google Calendar,
// and two confirmed appointments can never overlap (hazem.book_appointment, one lock).

/// <summary>B-23: the services booked from the calendar, their length and price (the lawyer's list).</summary>
public sealed record BookingService(string Code, string Short, int Minutes, int Amount, string Name)
{
    public static BookingService PhoneVideo { get; } = new("consultation_phone_video", "pv", 15, 500, "استشارة بالتليفون أو الفيديو");
    public static BookingService Office { get; } = new("consultation_office", "of", 30, 1000, "مقابلة في المكتب");
    public static BookingService Urgent { get; } = new("consultation_urgent", "ur", 15, 1000, "استشارة عاجلة بالتليفون");

    public static IReadOnlyList<BookingService> All { get; } = [PhoneVideo, Office, Urgent];

    public bool IsUrgent => Code == Urgent.Code;

    public static BookingService? ByCode(string? code) => All.FirstOrDefault(s => s.Code == code || s.Short == code);
}

/// <summary>The booking.* and office.* settings, read in every turn (the panel's changes count at once).</summary>
public sealed record BookingSettings(bool Enabled, BookingRules Rules, string CalendarId, OfficeInfo Office, HoldRules Hold)
{
    public const string HoldMinutesKey = "booking.hold_minutes";
    public const string HoldShortMinutesKey = "booking.hold_short_minutes";
    public const string HoldShortWithinKey = "booking.hold_short_within_hours";
    public const string HoldGapKey = "booking.hold_gap_minutes";

    public const string EnabledKey = "booking.enabled";
    public const string BufferKey = "booking.buffer_minutes";
    public const string MinNoticeKey = "booking.min_notice_hours";
    public const string HorizonKey = "booking.horizon_days";
    public const string MaxPerDayKey = "booking.max_per_day";
    public const string StepKey = "booking.slot_step_minutes";
    public const string OfferCountKey = "booking.offer_count";
    public const string CalendarIdKey = "booking.calendar_id";
    public const string GoogleBaseUrlKey = "booking.google_base_url";
    public const string ConflictAutoMessageKey = "booking.conflict_auto_message";

    public static IReadOnlyList<string> RuleKeys { get; } = [BufferKey, MinNoticeKey, HorizonKey, MaxPerDayKey, StepKey, OfferCountKey];

    public static BookingSettings From(HazemSettings s)
    {
        var d = BookingRules.Default;
        int Int(string key, int fallback) => int.TryParse(s.Get(key), out var v) ? v : fallback;
        var rules = new BookingRules(
            Int(BufferKey, d.BufferMinutes),
            Int(MinNoticeKey, d.MinNoticeHours),
            Int(HorizonKey, d.HorizonDays),
            int.TryParse(s.Get(MaxPerDayKey), out var max) && max > 0 ? max : null,
            Int(StepKey, d.SlotStepMinutes),
            Int(OfferCountKey, d.OfferCount));
        var h = HoldRules.Default;
        var hold = new HoldRules(Int(HoldMinutesKey, h.Minutes), Int(HoldShortMinutesKey, h.ShortMinutes), Int(HoldShortWithinKey, h.ShortWithinHours),
            Int(HoldGapKey, h.GapMinutes));
        return new(s.Flag(EnabledKey), rules.Problem() is null ? rules : d, s.Get(CalendarIdKey), OfficeInfo.From(s),
            hold.Problem() is null ? hold : h);
    }
}

/// <summary>
/// B-08 (1.13): how long a picked time waits for the receipt image. <see cref="Minutes"/>, or
/// <see cref="ShortMinutes"/> when the appointment starts within <see cref="ShortWithinHours"/>, and always
/// <see cref="GapMinutes"/> before it starts. A time whose hold would be shorter than
/// <see cref="ShortestHold"/> is not offered: the client could not pay in time.
/// </summary>
public sealed record HoldRules(int Minutes, int ShortMinutes, int ShortWithinHours, int GapMinutes)
{
    public static HoldRules Default { get; } = new(120, 60, 6, 15);

    public static TimeSpan ShortestHold { get; } = TimeSpan.FromMinutes(15);

    /// <summary>The worker reminds this long before the hold ends, when the hold is at least <see cref="RemindedHold"/> long.</summary>
    public const int ReminderBeforeMinutes = 30;

    public const int RemindedHold = 45;

    public DateTimeOffset Until(DateTimeOffset now, DateTimeOffset start)
    {
        var length = start - now < TimeSpan.FromHours(ShortWithinHours) ? ShortMinutes : Minutes;
        var until = now.AddMinutes(length);
        var latest = start.AddMinutes(-GapMinutes);
        return until < latest ? until : latest;
    }

    public bool Offerable(DateTimeOffset now, DateTimeOffset start) => Until(now, start) - now >= ShortestHold;

    /// <summary>The limits the panel accepts.</summary>
    public string? Problem() =>
        Minutes is < 15 or > 1440 ? "hold_minutes"
        : ShortMinutes < 15 || ShortMinutes > Minutes ? "hold_short_minutes"
        : ShortWithinHours is < 0 or > 72 ? "hold_short_within_hours"
        : GapMinutes is < 0 or > 240 ? "hold_gap_minutes"
        : null;
}

/// <summary>P1: the office's payment and address details, written from the panel.</summary>
public sealed record OfficeInfo(string InstaPay, string WalletNumber, string WalletProvider, string Address, string MapUrl)
{
    public const string InstaPayKey = "office.instapay";
    public const string WalletNumberKey = "office.wallet_number";
    public const string WalletProviderKey = "office.wallet_provider";
    public const string AddressKey = "office.address";
    public const string MapUrlKey = "office.map_url";

    public static IReadOnlyList<string> Keys { get; } = [InstaPayKey, WalletNumberKey, WalletProviderKey, AddressKey, MapUrlKey];

    public static OfficeInfo From(HazemSettings s) =>
        new(s.Get(InstaPayKey), s.Get(WalletNumberKey), s.Get(WalletProviderKey), s.Get(AddressKey), s.Get(MapUrlKey));

    /// <summary>B-08: the confirmation names both ways to pay; without them it is not sent.</summary>
    public bool HasPayment => InstaPay.Length > 0 && WalletNumber.Length > 0;

    public string Wallet => WalletProvider.Length > 0 ? $"{WalletNumber} ({WalletProvider})" : WalletNumber;
}

/// <summary>A row of hazem.appointments, as the flow and the panel need it.</summary>
public sealed record AppointmentInfo(
    Guid Id, Guid PersonId, string Service, DateTimeOffset Start, DateTimeOffset End, string State, int Reschedules, int Amount,
    string? MatterType, DateTimeOffset? HoldUntil = null)
{
    public const string PendingPayment = "pending_payment"; // 1.13: held until the receipt
    public const string Expired = "expired";                // 1.13: the hold's time ran out
    public const string Confirmed = "confirmed";
    public const string Completed = "completed";
    public const string NoShow = "no_show";
    public const string CancelledByClient = "cancelled_by_client";
    public const string CancelledByOffice = "cancelled_by_office";

    public BusyInterval Interval => new(Start, End);

    /// <summary>Held or confirmed: the client's booking that is still to come.</summary>
    public bool IsOpen => State is PendingPayment or Confirmed;
}

public enum BookStatus
{
    Held,       // 1.13: held until the receipt
    BookedPaid, // 1.13: confirmed at once, a receipt he already sent pays for it
    Exists,
    AlreadyBooked,
    HasHold,    // P3: one hold at a time
    Taken,
}

public sealed record BookOutcome(BookStatus Status, Guid? AppointmentId);

/// <summary>S-04 as the flow needs it. <see cref="ProofAt"/>: the first receipt («وقت الدفع», B-22).</summary>
public sealed record PaymentInfo(string State, int AmountDue, int? AmountReceived, DateTimeOffset? ProofAt)
{
    public const string Requested = "requested";
    public const string ProofReceived = "proof_received";
    public const string DuplicateSuspected = "duplicate_suspected";
    public const string Verified = "verified";
    public const string Partial = "partial";
    public const string NotReceived = "not_received";
    public const string RefundRequested = "refund_requested";
    public const string Refunded = "refunded";
}

/// <summary>A receipt image from the client (T-09: its place in storage only, never its content).</summary>
public sealed record ReceiptRequest(Guid PersonId, string Wamid, string Bucket, string Key, string Sha256, string? MimeType);

public enum ReceiptStatus
{
    None,          // nothing waits for a receipt
    Confirmed,     // the hold is confirmed
    ConfirmedLate, // the hold had run out, the time was still free: confirmed
    LateTaken,     // the hold had run out and the time was taken: kept for the next time he picks
    Received,      // a confirmed booking's (new) receipt
    Exists,        // the same message again
}

public sealed record ReceiptOutcome(ReceiptStatus Status, Guid? AppointmentId);

public enum MoveStatus
{
    Moved,
    Exists,
    Taken,
    NotAllowed,
    NotFound,
}

public sealed record BookRequest(Guid Id, Guid PersonId, Guid? IntakeId, BookingService Service, DateTimeOffset Start, string TurnId,
    int BufferMinutes, string? MatterType, DateTimeOffset HoldUntil);

public interface IBookingStore
{
    /// <summary>The week and the exceptions of the dates in [from, to].</summary>
    Task<Schedule> LoadScheduleAsync(DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Appointments that close their time (confirmed, or held until the receipt) and overlap [from, to).</summary>
    Task<IReadOnlyList<AppointmentInfo>> TakenBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>The client's next open appointment (held or confirmed), if any.</summary>
    Task<AppointmentInfo?> UpcomingForAsync(Guid personId, DateTimeOffset now, CancellationToken ct);

    Task<AppointmentInfo?> FindAsync(Guid id, CancellationToken ct);

    Task<BookOutcome> BookAsync(BookRequest request, CancellationToken ct);

    Task<MoveStatus> MoveAsync(Guid id, DateTimeOffset start, DateTimeOffset end, string turnId, int bufferMinutes, CancellationToken ct);

    /// <summary>held or confirmed → <paramref name="state"/>; the calendar event goes. False when it was neither.</summary>
    Task<bool> CancelAsync(Guid id, string state, CancellationToken ct);

    /// <summary>
    /// B-05 (1.13): what a receipt image from this client would be for. A hold whose time ran out comes
    /// back as such (<see cref="AppointmentInfo.Expired"/>): the desk checks its time before attaching.
    /// </summary>
    Task<AppointmentInfo?> ReceiptTargetAsync(Guid personId, DateTimeOffset now, CancellationToken ct);

    Task<ReceiptOutcome> AttachReceiptAsync(ReceiptRequest receipt, bool lateFree, int bufferMinutes, CancellationToken ct);

    Task<PaymentInfo?> PaymentAsync(Guid appointmentId, CancellationToken ct);

    /// <summary>B-22: a refund is due; the amount, or null when no money came for it.</summary>
    Task<int?> RequestRefundAsync(Guid appointmentId, string reason, CancellationToken ct);
}

/// <summary>The lawyer's calendar (T-14). Never throws: «unavailable» means Hazem offers nothing.</summary>
public interface IBusyCalendar
{
    Task<CalendarBusy> BusyAsync(DateTimeOffset from, DateTimeOffset to, HazemSettings settings, CancellationToken ct);
}

public sealed record CalendarBusy(bool Available, IReadOnlyList<BusyInterval> Busy, string? Problem = null)
{
    public static CalendarBusy Unavailable(string problem) => new(false, [], problem);
}

public sealed record FreeSlots(bool CalendarAvailable, IReadOnlyList<Slot> Slots);

/// <summary>Where the flow and the panel ask for free times and book (B-08, B-09, B-15).</summary>
public sealed class BookingDesk(IBookingStore store, IBusyCalendar calendar)
{
    public static TimeZoneInfo Zone => OfficeHours.Cairo;

    public async Task<FreeSlots> FindAsync(BookingSettings booking, HazemSettings settings, BookingService service, DateTimeOffset now,
        DateTimeOffset after, DateOnly? onlyDay, int limit, Guid? except, CancellationToken ct, bool holdCheck = true)
    {
        var rules = booking.Rules;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);
        // One day asked (re-checking a time, the urgent path): only that day, and the buffer around it.
        var (first, last) = onlyDay is { } day ? (day, day) : (today, today.AddDays(rules.HorizonDays));
        var margin = TimeSpan.FromMinutes(rules.BufferMinutes);
        var from = Instant(first) - margin;
        var to = Instant(last.AddDays(1)) + margin;
        var busy = await calendar.BusyAsync(from, to, settings, ct);
        if (!busy.Available)
        {
            return new FreeSlots(false, []);
        }

        var schedule = await store.LoadScheduleAsync(first, last, ct);
        var booked = (await store.TakenBetweenAsync(from, to, ct)).Where(a => a.Id != except).Select(a => a.Interval).ToList();
        // 1.13: a time too close to pay for in time is not offered (its hold would be under 15 minutes).
        // Only the first times of the day can be that close, so a second page is enough.
        var slots = new List<Slot>();
        var next = after;
        for (var page = 0; page < 2 && slots.Count < limit; page++)
        {
            var found = Availability.Find(schedule, rules, new SlotQuery(service.Minutes, next, service.IsUrgent, onlyDay, limit), booked, busy.Busy,
                now, Zone);
            slots.AddRange(found.Where(s => !holdCheck || booking.Hold.Offerable(now, s.Start)).Take(limit - slots.Count));
            if (found.Count < limit)
            {
                break;
            }

            next = found[^1].Start;
        }

        return new FreeSlots(true, slots);
    }

    /// <summary>B-08.3: the time is checked again (the calendar now, the panel's hours now), then booked atomically.</summary>
    public async Task<BookOutcome?> BookAsync(BookingSettings booking, HazemSettings settings, BookRequest request, DateTimeOffset now,
        CancellationToken ct)
    {
        if (!await StillFreeAsync(booking, settings, request.Service, request.Start, now, null, ct))
        {
            return null;
        }

        return await store.BookAsync(request, ct);
    }

    public async Task<MoveStatus> MoveAsync(BookingSettings booking, HazemSettings settings, AppointmentInfo appointment, DateTimeOffset start,
        string turnId, DateTimeOffset now, CancellationToken ct)
    {
        var service = BookingService.ByCode(appointment.Service) ?? BookingService.PhoneVideo;
        if (!await StillFreeAsync(booking, settings, service, start, now, appointment.Id, ct, holdCheck: false))
        {
            return MoveStatus.Taken;
        }

        return await store.MoveAsync(appointment.Id, start, start.AddMinutes(service.Minutes), turnId, booking.Rules.BufferMinutes, ct);
    }

    private async Task<bool> StillFreeAsync(BookingSettings booking, HazemSettings settings, BookingService service, DateTimeOffset start,
        DateTimeOffset now, Guid? except, CancellationToken ct, bool holdCheck = true)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, Zone).DateTime);
        var found = await FindAsync(booking, settings, service, now, start.AddTicks(-1), day, 1, except, ct, holdCheck);
        return found.CalendarAvailable && found.Slots.Count == 1 && found.Slots[0].Start == start;
    }

    /// <summary>
    /// B-05 (1.13): a receipt image. A hold whose time ran out is confirmed again only if its time is
    /// still free now (the calendar and the panel's hours, then the database inside the SQL function).
    /// Null: nothing of this client waits for a receipt.
    /// </summary>
    public async Task<ReceiptOutcome?> ReceiptAsync(BookingSettings booking, HazemSettings settings, ReceiptRequest receipt, DateTimeOffset now,
        CancellationToken ct)
    {
        if (await store.ReceiptTargetAsync(receipt.PersonId, now, ct) is not { } target)
        {
            return null;
        }

        var lateFree = target.State == AppointmentInfo.Expired
                       && await StillFreeAsync(booking, settings, BookingService.ByCode(target.Service) ?? BookingService.PhoneVideo, target.Start, now,
                           target.Id, ct, holdCheck: false);
        var outcome = await store.AttachReceiptAsync(receipt, lateFree, booking.Rules.BufferMinutes, ct);
        return outcome.Status == ReceiptStatus.None ? null : outcome;
    }

    /// <summary>P7: does anything of this client wait for a receipt (a PDF is then answered «ابعت صورة»).</summary>
    public async Task<bool> WaitsForReceiptAsync(Guid personId, DateTimeOffset now, CancellationToken ct) =>
        await store.ReceiptTargetAsync(personId, now, ct) is not null;

    public Task<PaymentInfo?> PaymentAsync(Guid appointmentId, CancellationToken ct) => store.PaymentAsync(appointmentId, ct);

    public Task<int?> RequestRefundAsync(Guid appointmentId, string reason, CancellationToken ct) => store.RequestRefundAsync(appointmentId, reason, ct);

    public Task<AppointmentInfo?> UpcomingForAsync(Guid personId, DateTimeOffset now, CancellationToken ct) =>
        store.UpcomingForAsync(personId, now, ct);

    public Task<AppointmentInfo?> FindAppointmentAsync(Guid id, CancellationToken ct) => store.FindAsync(id, ct);

    public Task<bool> CancelAsync(Guid id, string state, CancellationToken ct) => store.CancelAsync(id, state, ct);

    /// <summary>The instant a Cairo date starts.</summary>
    public static DateTimeOffset Instant(DateOnly day) => Availability.At(day, 0, Zone) ?? Availability.At(day, 60, Zone)!.Value;

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);
}

/// <summary>
/// The ids carried by the WhatsApp buttons and list rows (≤ 200 characters). Hazem reads them without
/// the AI: «bk:m:pv» the kind of consultation, «bk:s:pv:1793000000» a time, «bk:n:…» more times,
/// «bk:r:id» change, «bk:t:id:…» the new time, «bk:u:id:…» more new times, «bk:c:id» cancel,
/// «bk:f:id» refund.
/// </summary>
public sealed record BookingChoice(char Kind, BookingService? Service, Guid? AppointmentId, DateTimeOffset? At)
{
    public const string Prefix = "bk:";

    public static string Mode(BookingService s) => $"bk:m:{s.Short}";

    public static string Pick(BookingService s, DateTimeOffset at) => $"bk:s:{s.Short}:{at.ToUnixTimeSeconds()}";

    public static string More(BookingService s, DateTimeOffset after) => $"bk:n:{s.Short}:{after.ToUnixTimeSeconds()}";

    public static string Change(Guid id) => $"bk:r:{id:N}";

    public static string MoveTo(Guid id, DateTimeOffset at) => $"bk:t:{id:N}:{at.ToUnixTimeSeconds()}";

    public static string MoreMoves(Guid id, DateTimeOffset after) => $"bk:u:{id:N}:{after.ToUnixTimeSeconds()}";

    public static string Cancel(Guid id) => $"bk:c:{id:N}";

    public static string Refund(Guid id) => $"bk:f:{id:N}";

    public static BookingChoice? Parse(string? id)
    {
        if (id is null || !id.StartsWith(Prefix, StringComparison.Ordinal) || id.Length > 200)
        {
            return null;
        }

        var p = id.Split(':');
        if (p.Length < 3 || p[1].Length != 1)
        {
            return null;
        }

        var kind = p[1][0];
        DateTimeOffset? At(string s) => long.TryParse(s, out var v) && v is > 1_600_000_000 and < 4_000_000_000 ? DateTimeOffset.FromUnixTimeSeconds(v) : null;
        Guid? Id(string s) => Guid.TryParseExact(s, "N", out var g) ? g : null;
        return kind switch
        {
            'm' when p.Length == 3 && BookingService.ByCode(p[2]) is { } s => new(kind, s, null, null),
            's' or 'n' when p.Length == 4 && BookingService.ByCode(p[2]) is { } s && At(p[3]) is { } at => new(kind, s, null, at),
            'r' or 'c' or 'f' when p.Length == 3 && Id(p[2]) is { } g => new(kind, null, g, null),
            't' or 'u' when p.Length == 4 && Id(p[2]) is { } g && At(p[3]) is { } at => new(kind, null, g, at),
            _ => null,
        };
    }
}

/// <summary>Days and times as Hazem writes them (the API has no culture data: written here).</summary>
public static class BookingWords
{
    private static readonly string[] Days = ["الأحد", "الاتنين", "التلات", "الأربع", "الخميس", "الجمعة", "السبت"];

    /// <summary>The weekday as Hazem and the office's e-mails write it.</summary>
    public static string DayName(DayOfWeek day) => Days[(int)day];

    /// <summary>«النهارده»، «بكرة»، or «الخميس 29/10».</summary>
    public static string Day(DateTimeOffset at, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(at, BookingDesk.Zone);
        var day = DateOnly.FromDateTime(local.DateTime);
        var today = BookingDesk.Today(now);
        return day == today ? "النهارده" : day == today.AddDays(1) ? "بكرة" : $"{Days[(int)local.DayOfWeek]} {local.Day}/{local.Month}";
    }

    /// <summary>An amount as Hazem writes it: «1,000».</summary>
    public static string Money(int pounds) => pounds.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A hold's end as the client reads it after «قبل الساعة»: «4:30 م النهارده», «10:15 ص بكرة».</summary>
    public static string Deadline(DateTimeOffset at, DateTimeOffset now) => $"{Time(at)} {Day(at, now)}";

    /// <summary>«6:15 م».</summary>
    public static string Time(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, BookingDesk.Zone);
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        return $"{hour}:{local.Minute:00} {(local.Hour < 12 ? "ص" : "م")}";
    }

    /// <summary>A list row: «الخميس 29/10 6:15 م» (≤ 24 characters).</summary>
    public static string Row(DateTimeOffset at, DateTimeOffset now) => $"{Day(at, now)} {Time(at)}";
}
