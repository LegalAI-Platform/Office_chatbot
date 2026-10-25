using Office.Application.Booking;
using Office.Application.Texts;

namespace Office.Application.Reminders;

// TASK 12 (DECISIONS v1.16, B-18 / M-03): the reminders before a confirmed appointment. The day before, with
// «هحضر ✅» and «تغيير أو إلغاء», and two hours before. Holds never get them (1.13): only a confirmed booking.

public static class ReminderKinds
{
    public const string DayBefore = "day_before";
    public const string TwoHours = "two_hours";

    public static IReadOnlyList<string> All { get; } = [DayBefore, TwoHours];
}

/// <summary>Why a reminder will never go out (hazem.appointment_reminders.skip_reason).</summary>
public static class ReminderSkips
{
    public const string TooClose = "too_close"; // B-18: moved to 10:00, it would be under an hour before
    public const string Passed = "passed";      // B-18: its time had gone when the booking was confirmed (or moved)
    public const string Late = "late";          // the worker reached it too late for «بكرة» / «النهارده» or the hour before
    public const string Off = "off";            // its texts are not all approved
}

/// <summary>When a reminder goes out (<see cref="Due"/>), or why it never will (<see cref="Skip"/>).</summary>
public sealed record ReminderPlan(DateTimeOffset? Due, string? Skip)
{
    public static ReminderPlan At(DateTimeOffset due) => new(due, null);

    public static ReminderPlan Skipped(string why) => new(null, why);
}

/// <summary>B-18's timing, in Cairo time. Pure: the worker and the tests use the same rules.</summary>
public static class ReminderRules
{
    public static TimeSpan DayBeforeLead { get; } = TimeSpan.FromHours(24);
    public static TimeSpan TwoHoursLead { get; } = TimeSpan.FromHours(2);

    /// <summary>B-18: a reminder is never sent less than an hour before the appointment.</summary>
    public static TimeSpan Minimum { get; } = TimeSpan.FromHours(1);

    /// <summary>B-18: nothing goes out between midnight and 10:00; it waits for 10:00.</summary>
    public const int MorningHour = 10;

    /// <summary>
    /// B-18. The day before: 24 hours before; two hours before: 2 hours before, and never on the day before (its
    /// words say «النهارده»). Between midnight and 10:00 → 10:00; that leaves under an hour → skipped; already past
    /// when the booking is confirmed (or moved, <paramref name="now"/>) → skipped.
    /// </summary>
    public static ReminderPlan Plan(string kind, DateTimeOffset start, DateTimeOffset now)
    {
        var zone = BookingDesk.Zone;
        var due = start - (kind == ReminderKinds.DayBefore ? DayBeforeLead : TwoHoursLead);
        var day = Date(due);
        if (kind == ReminderKinds.TwoHours && day < Date(start))
        {
            day = Date(start);
            due = BookingDesk.Instant(day); // the start's own day, at midnight: then 10:00 below
        }

        if (TimeZoneInfo.ConvertTime(due, zone).Hour < MorningHour)
        {
            due = BookingDesk.Instant(day).AddHours(MorningHour);
        }

        if (start - due < Minimum)
        {
            return ReminderPlan.Skipped(ReminderSkips.TooClose);
        }

        return due <= now ? ReminderPlan.Skipped(ReminderSkips.Passed) : ReminderPlan.At(due);
    }

    /// <summary>
    /// Still true to send now (the worker may reach a reminder late): at least an hour before, and its words still
    /// right: «بكرة» on the day before, «النهارده» on the day itself.
    /// </summary>
    public static bool StillUseful(string kind, DateTimeOffset start, DateTimeOffset now) =>
        start - now >= Minimum && Date(start) == (kind == ReminderKinds.DayBefore ? Date(now).AddDays(1) : Date(now));

    private static DateOnly Date(DateTimeOffset at) => BookingDesk.Today(at);
}

/// <summary>
/// The ids of the reminder's buttons (B-18), read without the AI like the booking's: «bk:a:id:start» I will come,
/// «bk:e:id» change or cancel. The time in «هحضر» tells an old reminder of a moved appointment from the current one.
/// </summary>
public static class ReminderButtons
{
    public static string Attend(Guid appointment, DateTimeOffset start) => $"bk:a:{appointment:N}:{start.ToUnixTimeSeconds()}";

    public static string Change(Guid appointment) => $"bk:e:{appointment:N}";
}

/// <summary>What a reminder says and how the M-03 template carries it outside the 24-hour window.</summary>
public static class ReminderTexts
{
    /// <summary>Every text a reminder needs: without one of them none is sent (never half a reminder).</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        TextKeys.ReminderDayBefore, TextKeys.Reminder2hBefore, TextKeys.BtnReminderAttend, TextKeys.BtnReminderChange,
        TextKeys.ReminderAttendOk, TextKeys.ReminderDetailsOffice, TextKeys.ReminderDetailsPhone, TextKeys.ReminderDetailsUrf,
        TextKeys.ReminderOldButton,
    ];

    /// <summary>The M-03 template's name (n8n/templates/whatsapp-templates.json).</summary>
    public static string Template(string kind) => kind == ReminderKinds.DayBefore ? "reminder_day_before" : "reminder_2h_before";

    public static string TextKey(string kind) => kind == ReminderKinds.DayBefore ? TextKeys.ReminderDayBefore : TextKeys.Reminder2hBefore;

    /// <summary>The details line: the office's address and map, the lawyer's call, or who comes to an Urfi appointment.</summary>
    public static (string Key, Dictionary<string, string> Fill) Details(BookingService service, OfficeInfo office, string? urfAttendees)
    {
        var place = new Dictionary<string, string>();
        if (office.Address.Trim().Length > 0)
        {
            place["address"] = office.Address.Trim();
        }

        if (office.MapUrl.Trim().Length > 0)
        {
            place["map"] = office.MapUrl.Trim();
        }

        if (service.IsUrf)
        {
            if (urfAttendees is not null)
            {
                place["attendees"] = urfAttendees;
            }

            return (TextKeys.ReminderDetailsUrf, place);
        }

        return service.Code == BookingService.Office.Code ? (TextKeys.ReminderDetailsOffice, place) : (TextKeys.ReminderDetailsPhone, []);
    }
}
