using System.Globalization;
using Office.Application.Booking;
using Office.Application.Responders;
using Office.Application.Services;
using Office.Application.Texts;

namespace Office.Application.Ratings;

// v1.31 (DECISIONS B-26): the service rating, once per finished service. A consultation (office or phone / video) or an
// Urfi contract: 2 hours after the office marks it completed; a written service: 24 hours after its delivery. Never to a
// muted conversation (S-01), an urgent consultation or an Urfi divorce, and one person at most once in 30 days. The
// client's row is read by code (never the AI), like the booking's and the reminders' buttons.

/// <summary>Why a rating will never go out (hazem.service_ratings.skip_reason).</summary>
public static class RatingSkips
{
    public const string Off = "off";               // rating.enabled is false, or its texts are not all approved
    public const string Muted = "muted";           // S-01: the conversation is muted
    public const string Recent = "recent";         // the person was asked within rating.repeat_days
    public const string Late = "late";             // the worker reached it more than a day after its time
    public const string NoTemplate = "no_template"; // outside the 24-hour window, service_rating not approved (yet)
}

/// <summary>B-26's numbers, from hazem.settings (the defaults are the owner's).</summary>
public sealed record RatingSettings(bool Enabled, TimeSpan AfterAppointment, TimeSpan AfterDelivery, TimeSpan Repeat, DateTimeOffset? Since)
{
    public const string EnabledKey = "rating.enabled";
    public const string AfterAppointmentKey = "rating.after_appointment_hours";
    public const string AfterDeliveryKey = "rating.after_delivery_hours";
    public const string RepeatKey = "rating.repeat_days";

    /// <summary>Only services finished after this time get a rating (migration 0033 sets it when it runs).</summary>
    public const string SinceKey = "rating.since";

    public static RatingSettings Default { get; } = new(true, TimeSpan.FromHours(2), TimeSpan.FromHours(24), TimeSpan.FromDays(30), null);

    public static RatingSettings From(HazemSettings s)
    {
        int Int(string key, int fallback, int max) => int.TryParse(s.Get(key), out var v) && v >= 0 && v <= max ? v : fallback;
        return new(
            !s.Get(EnabledKey, "true").Equals("false", StringComparison.OrdinalIgnoreCase), // on unless switched off
            TimeSpan.FromHours(Int(AfterAppointmentKey, 2, 24 * 7)),
            TimeSpan.FromHours(Int(AfterDeliveryKey, 24, 24 * 7)),
            TimeSpan.FromDays(Int(RepeatKey, 30, 365)),
            DateTimeOffset.TryParse(s.Get(SinceKey), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since)
                ? since.ToUniversalTime()
                : null);
    }
}

/// <summary>B-26's rules. Pure: the worker and the tests use the same ones.</summary>
public static class RatingRules
{
    /// <summary>The appointments that are rated once completed: never the urgent consultation, never an Urfi divorce.</summary>
    public static IReadOnlyList<string> AppointmentServices { get; } =
    [
        BookingService.PhoneVideo.Code, BookingService.Office.Code, BookingService.UrfMarriageOffice.Code, BookingService.UrfMarriageOwn.Code,
    ];

    /// <summary>The written services, rated once delivered (B-16, B-17).</summary>
    public static IReadOnlyList<string> OrderServices { get; } = ServiceKind.All.Select(k => k.Code).ToList();

    /// <summary>A rating reached more than this after its time is no longer about a fresh service: skipped.</summary>
    public static TimeSpan MaxLate { get; } = TimeSpan.FromDays(1);

    public static bool IsRated(string service) => AppointmentServices.Contains(service) || OrderServices.Contains(service);

    /// <summary>B-26: 2 hours after an appointment was completed, 24 hours after an order was delivered (rating.*).</summary>
    public static DateTimeOffset Due(string service, DateTimeOffset finishedAt, RatingSettings s) =>
        finishedAt + (OrderServices.Contains(service) ? s.AfterDelivery : s.AfterAppointment);

    /// <summary>
    /// Why the rating that came due is not sent (null: it goes). <paramref name="lastAsked"/>: the last rating this person
    /// was sent (any service). In this order: off, muted, recent, late.
    /// </summary>
    public static string? Skip(RatingSettings s, bool textsOn, bool muted, DateTimeOffset? lastAsked, DateTimeOffset due, DateTimeOffset now)
    {
        if (!s.Enabled || !textsOn)
        {
            return RatingSkips.Off;
        }

        if (muted)
        {
            return RatingSkips.Muted;
        }

        if (lastAsked is { } asked && now - asked < s.Repeat)
        {
            return RatingSkips.Recent;
        }

        return now - due > MaxLate ? RatingSkips.Late : null;
    }

    /// <summary>
    /// {{service}} in rating_ask («… في [الخدمة]»): a consultation is «الاستشارة», an Urfi contract «عقد الجواز العرفي», a
    /// written service its name in the service texts (<see cref="ServiceKind"/>: «مراجعة العقد»، «صياغة العقد»…).
    /// </summary>
    public static string ServiceName(string service) =>
        ServiceKind.ByCode(service)?.Name
        ?? (service.StartsWith("urf_marriage", StringComparison.Ordinal) ? "عقد الجواز العرفي" : "الاستشارة");
}

/// <summary>A row the client pressed: <see cref="Score"/> 5, 4, 3 or 2, or null for «ليس الآن».</summary>
public sealed record RatingPress(Guid RatingId, int? Score)
{
    public bool IsLow => Score == RatingButtons.Low;
}

/// <summary>
/// The ids of the rating's rows (and of the template's quick replies): «rt:id:5» … «rt:id:2», «rt:id:0» for «ليس الآن».
/// Read without the AI; the id names the rating, so an old list is told from a new one.
/// </summary>
public static class RatingButtons
{
    public const string Prefix = "rt:";
    public const int Low = 2;
    private const int Later = 0;

    public static string Id(Guid rating, int? score) => $"{Prefix}{rating:N}:{score ?? Later}";

    public static RatingPress? Parse(string? id)
    {
        if (id is null || !id.StartsWith(Prefix, StringComparison.Ordinal) || id.Length > 200)
        {
            return null;
        }

        var p = id.Split(':');
        if (p.Length != 3 || !Guid.TryParseExact(p[1], "N", out var rating) || !int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out var score))
        {
            return null;
        }

        return score switch
        {
            Later => new RatingPress(rating, null),
            >= Low and <= 5 => new RatingPress(rating, score),
            _ => null,
        };
    }
}

/// <summary>What a rating says, its rows, and the M-03 template outside the 24-hour window.</summary>
public static class RatingTexts
{
    /// <summary>The M-03 template (n8n/templates/whatsapp-templates.json): {{1}} the service, the five rows as quick replies.</summary>
    public const string Template = "service_rating";

    /// <summary>The list's rows in order, each an approved text: «ممتاز» (5) … «يحتاج للتحسين» (2), «ليس الآن».</summary>
    public static IReadOnlyList<(int? Score, string Key)> Rows { get; } =
    [
        (5, TextKeys.RowRating5), (4, TextKeys.RowRating4), (3, TextKeys.RowRating3), (RatingButtons.Low, TextKeys.RowRating2),
        (null, TextKeys.RowRatingLater),
    ];

    /// <summary>Every text a rating needs: without one of them none is sent (never half a rating).</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        TextKeys.RatingAsk, TextKeys.BtnRating, TextKeys.RowRating5, TextKeys.RowRating4, TextKeys.RowRating3, TextKeys.RowRating2,
        TextKeys.RowRatingLater, TextKeys.RatingThanks, TextKeys.RatingLow, TextKeys.RatingLater,
    ];

    /// <summary>The reply to a press: 5, 4, 3 → thanks; 2 → «آسفين…» (and a task); «ليس الآن» → «تمام…».</summary>
    public static string Reply(int? score) => score switch
    {
        null => TextKeys.RatingLater,
        RatingButtons.Low => TextKeys.RatingLow,
        _ => TextKeys.RatingThanks,
    };
}

public enum RatingAnswerStatus
{
    Recorded, // the first answer (or a score after «ليس الآن»)
    Again,    // already answered: nothing changes
    Unknown,  // not this person's rating, or never sent
}

/// <summary>What a press did. <see cref="Score"/>: the rating's score now (null: «ليس الآن» or none).</summary>
public sealed record RatingAnswer(RatingAnswerStatus Status, int? Score);

/// <summary>hazem.service_ratings for the conversation (the worker writes the rest).</summary>
public interface IRatingStore
{
    /// <summary>
    /// Records the press of this person's rating: the first answer counts; after «ليس الآن» a score still counts.
    /// <paramref name="turnId"/>: the press's turn (its rating_low task is made with it).
    /// </summary>
    Task<RatingAnswer> AnswerAsync(Guid personId, RatingPress press, string turnId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// B-26: the client's next words, within 24 hours of «يحتاج للتحسين», are its reason: the rating_low task takes this
    /// turn (the panel shows its words). True when they were taken; Hazem then answers nothing more.
    /// </summary>
    Task<bool> TakeReasonAsync(Guid personId, string waId, string turnId, DateTimeOffset now, CancellationToken ct);
}
