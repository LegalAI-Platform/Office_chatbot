using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;

namespace Office.Application.Booking;

// v1.33 (B-09, the owner 2026-10-31): with the booking on, the urgent consultation is a call-back after the payment, not a
// time the client picks. The first free moment is held at once (urgent_pay); the receipt books the first free moment from
// then (urgent_now / urgent_after); the lawyer gets the template urgent_alert with «شوف التحويل»: the receipt itself, then
// «التحويل تمام، هكلمه» (urgent_seen to the client) or «فيه مشكلة» (payment_not_received; a new receipt alerts him again).
// No decision in time: the manager too (urgent_waiting to the client). The booking off: the urgent path as in 1.32.

/// <summary>
/// v1.33 (B-09): the urgent call's settings and the two numbers of «بيانات المكتب». v1.34 (B-09.6, test only):
/// <see cref="AlertInWindow"/>, the alert as a normal message when the number wrote in the last 24 hours.
/// </summary>
public sealed record UrgentRules(int CallWithinMinutes, int EscalateAfterMinutes, string LawyerWhatsApp, string ManagerWhatsApp,
    bool AlertInWindow = false)
{
    public const string CallWithinKey = "urgent.call_within_minutes";
    public const string EscalateAfterKey = "urgent.escalate_after_minutes";
    public const string LawyerWhatsAppKey = "office.lawyer_whatsapp";
    public const string ManagerWhatsAppKey = "office.manager_whatsapp";
    public const string AlertInWindowKey = "urgent.alert_in_window";

    public static UrgentRules Default { get; } = new(10, 10, "", "");

    /// <param name="testEnvironment">v1.34: the switch counts in the test environment only, whatever the setting says.</param>
    public static UrgentRules From(HazemSettings s, bool testEnvironment = false)
    {
        int Minutes(string key, int fallback) => int.TryParse(s.Get(key), out var v) && v is >= 1 and <= 120 ? v : fallback;
        return new(Minutes(CallWithinKey, Default.CallWithinMinutes), Minutes(EscalateAfterKey, Default.EscalateAfterMinutes),
            Digits(s.Get(LawyerWhatsAppKey)) ?? "", Digits(s.Get(ManagerWhatsAppKey)) ?? "", testEnvironment && s.Flag(AlertInWindowKey));
    }

    /// <summary>
    /// A WhatsApp number as the panel takes it (like office.phone: digits, spaces, dashes, a leading +) in WhatsApp's form:
    /// digits only, an Egyptian «01…» as «201…». Null when it is not a number (8 to 15 digits).
    /// </summary>
    public static string? Digits(string? raw)
    {
        var v = raw?.Trim() ?? "";
        if (v.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(v, @"^\+?[0-9 -]{6,20}$"))
        {
            return null;
        }

        var digits = new string(v.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith("01", StringComparison.Ordinal))
        {
            digits = "2" + digits;
        }

        return digits.Length is >= 8 and <= 15 ? digits : null;
    }

    /// <summary>
    /// The urgent hold waits for the receipt <see cref="HoldRules.ShortMinutes"/>, and at night until the first free time itself
    /// (the lawyer's first moment stays the client's): its time is set again when the receipt comes.
    /// </summary>
    public static DateTimeOffset HoldUntil(HoldRules hold, DateTimeOffset now, DateTimeOffset start)
    {
        var until = now.AddMinutes(hold.ShortMinutes);
        return start > until ? start : until;
    }
}

/// <summary>The first free moment for the urgent call; <see cref="Start"/> null: none within the horizon.</summary>
public sealed record UrgentFree(bool CalendarAvailable, DateTimeOffset? Start);

/// <summary>v1.33 (B-09): the new path runs only when every one of its texts is approved; otherwise urgent is as in 1.32.</summary>
public static class UrgentTexts
{
    /// <summary>The M-03 template (n8n/templates/whatsapp-templates.json): {{1}} the name, {{2}} the number, {{3}} the time, «شوف التحويل».</summary>
    public const string Template = "urgent_alert";

    public static IReadOnlyList<string> Keys { get; } =
    [
        TextKeys.UrgentPay, TextKeys.UrgentNight, TextKeys.UrgentNow, TextKeys.UrgentAfter, TextKeys.UrgentSeen, TextKeys.UrgentWaiting,
        TextKeys.UrgentAlert, TextKeys.BtnUrgentSee, TextKeys.BtnUrgentOk, TextKeys.BtnUrgentBad,
    ];

    /// <summary>urgent_night's {{opens}}: «10» for 10:00, «10:30» otherwise (it reads «الساعة 10 الصبح»).</summary>
    public static string Opens(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, BookingDesk.Zone);
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        return local.Minute == 0 ? $"{hour}" : $"{hour}:{local.Minute:00}";
    }

    /// <summary>The alert's {{2}}: the client's number as the lawyer dials it («01…» for an Egyptian number, else «+…»).</summary>
    public static string LocalNumber(string waId) =>
        waId.Length == 12 && waId.StartsWith("201", StringComparison.Ordinal) ? "0" + waId[2..] : "+" + waId;

    /// <summary>The alert's {{1}}: the stored name, else the WhatsApp contact name, else «عميل».</summary>
    public static string Name(string? stored, string? contact) =>
        stored?.Trim() is { Length: > 0 } s ? s : contact?.Trim() is { Length: > 0 } c ? c : "عميل";
}

/// <summary>What the lawyer (or the manager) pressed: <see cref="Id"/> is the call for «see», the receipt for «ok» and «bad».</summary>
public sealed record UrgentPress(char Kind, Guid Id)
{
    public const char See = 's';
    public const char Ok = 'o';
    public const char Bad = 'b';
}

/// <summary>
/// v1.33 (B-09): the urgent alert's buttons, read by code: «ug:see:id» the template's quick reply «شوف التحويل» (the call),
/// «ug:ok:id» «التحويل تمام، هكلمه» and «ug:bad:id» «فيه مشكلة» under the receipt image (the receipt itself).
/// </summary>
public static class UrgentButtons
{
    public const string Prefix = "ug:";

    public static string See(Guid appointmentId) => $"ug:see:{appointmentId:N}";

    public static string Ok(Guid receiptId) => $"ug:ok:{receiptId:N}";

    public static string Bad(Guid receiptId) => $"ug:bad:{receiptId:N}";

    public static UrgentPress? Parse(string? id)
    {
        if (id is null || !id.StartsWith(Prefix, StringComparison.Ordinal) || id.Split(':') is not [_, var kind, var g]
            || !Guid.TryParseExact(g, "N", out var guid))
        {
            return null;
        }

        return kind switch
        {
            "see" => new UrgentPress(UrgentPress.See, guid),
            "ok" => new UrgentPress(UrgentPress.Ok, guid),
            "bad" => new UrgentPress(UrgentPress.Bad, guid),
            _ => null,
        };
    }

    /// <summary>The urgent alert's button when it is the turn's last press.</summary>
    public static UrgentPress? In(Turn turn)
    {
        var last = turn.Messages.LastOrDefault(m => m.Interactive?.Id is not null || m.Button?.Payload is not null);
        return Parse(last?.Interactive?.Id ?? last?.Button?.Payload);
    }
}

/// <summary>
/// v1.33 (B-09): the urgent alert's buttons, from any number (the lawyer, or the manager after the escalation). «see»: the call's
/// latest receipt still without a decision, as a document, then the alert's words with «تمام» and «فيه مشكلة». «ok» / «bad»: the
/// first decision on the call's latest receipt wins (urgent_seen, or payment_not_received and a new receipt alerts again); a
/// stale press changes nothing and tells the client nothing, and a newer undecided receipt is sent to the presser instead.
/// False: nothing was decided.
/// </summary>
public interface IUrgentCalls
{
    Task<bool> PressAsync(UrgentPress press, string presserWaId, string turnId, CancellationToken ct);
}
