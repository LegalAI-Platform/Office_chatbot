using Office.Application.Booking;
using Office.Application.Services;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Domain.Clients;

namespace Office.Application.Conversations;

// v1.39 (TASK 16): the client's situation, read before every reply. A general message («السلام عليكم»، «عايز استشارة»،
// «فاكرني؟»…) from a client with something open gets his situation first, never a new offer; and a few rules the code
// keeps whatever the AI read (never a paid offer while one is held or paid, never the same answer twice in a row).

/// <summary>The newest open item, in the order the owner set: an urgent call paid and waiting, a time held for the
/// transfer, a confirmed appointment to come, a written service order; nothing.</summary>
public enum SituationKind
{
    None,
    UrgentWaiting,
    HeldUnpaid,
    Confirmed,
    Order,
}

/// <summary>
/// An appointment still to come (held or confirmed), as the situation needs it. v1.45: <paramref name="Reviewed"/> false while
/// its transfer waits for the office's review (the photo came, not verified yet).
/// </summary>
public sealed record OpenAppointment(string Service, string State, DateTimeOffset Start, DateTimeOffset? HoldUntil, bool Reviewed = true);

/// <summary>A written service order not delivered yet (S-07: collecting, awaiting_price, awaiting_payment, in_progress).</summary>
public sealed record OpenOrder(string Kind, string State);

/// <summary>
/// What the database knows: the client's next appointment, his newest open order, and the approved texts of his last
/// answered turn (<see cref="LastReplyKeys"/>: the forbidden rule 2 and the wordings taken in turn). v1.47 (B-32):
/// <see cref="Waiting"/>, the buttons or list rows that turn ended with (its last message), still waiting for his choice.
/// </summary>
public sealed record SituationFacts(OpenAppointment? Appointment, OpenOrder? Order, IReadOnlyList<string> LastReplyKeys,
    IReadOnlyList<InteractiveChoice>? Waiting = null)
{
    public static SituationFacts Empty { get; } = new(null, null, []);
}

public sealed record ClientSituation(SituationKind Kind, SituationFacts Facts)
{
    public static ClientSituation None { get; } = new(SituationKind.None, SituationFacts.Empty);

    public static ClientSituation From(SituationFacts f) => new(f.Appointment switch
    {
        { State: AppointmentInfo.Confirmed } a when a.Service == BookingService.Urgent.Code => SituationKind.UrgentWaiting,
        { State: AppointmentInfo.PendingPayment } => SituationKind.HeldUnpaid,
        { State: AppointmentInfo.Confirmed } => SituationKind.Confirmed,
        _ when f.Order is not null => SituationKind.Order,
        _ => SituationKind.None,
    }, f);

    public bool HasOpenItem => Kind != SituationKind.None;

    /// <summary>The forbidden rule 1: a consultation held, paid or waiting for its call: no paid offer, no payment asked.</summary>
    public bool BlocksPaidOffer => Kind is SituationKind.UrgentWaiting or SituationKind.HeldUnpaid or SituationKind.Confirmed;

    public bool RepliedLast(string key) => Facts.LastReplyKeys.Contains(key);

    /// <summary>The situation reply (texts 1–4 of TASK 16), filled; null when there is nothing open.</summary>
    public ReplyStep? Reply(string call, DateTimeOffset now, int callWithinMinutes)
    {
        var fill = new Dictionary<string, string> { ["call"] = call };
        switch (Kind)
        {
            case SituationKind.UrgentWaiting:
                var a = Facts.Appointment!;
                fill["when"] = a.Start <= now.AddMinutes(callWithinMinutes) ? "خلال دقايق"
                    : BookingDesk.Today(a.Start) == BookingDesk.Today(now) ? $"حوالي الساعة {BookingWords.Time(a.Start)}"
                    : $"{BookingWords.Day(a.Start, now)} الساعة {BookingWords.Time(a.Start)}";
                return new ReplyStep(TextKeys.SituationUrgent, null, fill);
            case SituationKind.HeldUnpaid or SituationKind.Confirmed:
                var h = Facts.Appointment!;
                fill["service"] = (BookingService.ByCode(h.Service) ?? BookingService.PhoneVideo).Name;
                fill["day"] = BookingWords.Day(h.Start, now);
                fill["time"] = BookingWords.Time(h.Start);
                if (Kind == SituationKind.Confirmed && !h.Reviewed)
                {
                    // v1.45: the photo came, the office has not reviewed it: held for him, never «متأكد» yet
                    return new ReplyStep(TextKeys.SituationReview, TextKeys.SituationConfirmed, fill);
                }

                if (Kind == SituationKind.Confirmed)
                {
                    return new ReplyStep(TextKeys.SituationConfirmed, null, fill);
                }

                fill["deadline"] = BookingWords.Deadline(h.HoldUntil ?? h.Start, now);
                return new ReplyStep(TextKeys.SituationHeld, null, fill);
            case SituationKind.Order:
                var o = Facts.Order!;
                fill["service"] = ServiceKind.ByCode(o.Kind)?.Name ?? "الخدمة";
                fill["status"] = o.State switch
                {
                    "collecting" => "مستني الورق",
                    "awaiting_price" => "الأستاذ بيراجعه ويحدد السعر",
                    "awaiting_payment" => "مستني التحويل",
                    _ => "شغالين عليه",
                };
                return new ReplyStep(TextKeys.SituationOrder, null, fill);
            default:
                return null;
        }
    }
}

/// <summary>
/// v1.39 (TASK 16, 3.6): how Hazem calls the client, from the name he TYPED (never his WhatsApp name): a kunya as he wrote
/// it («يا أبو عماد»), «يا أستاذ محمد» / «يا أستاذة منى» when the gender of the name is clear, and nothing otherwise (the
/// sentence then reads without it). The value goes into {{call}} with its leading space.
/// </summary>
public static class Salutation
{
    private static readonly string[] Kunya = ["أبو", "ابو", "أم", "ام"];

    public static string Call(Person person)
    {
        var words = person.Name?.Raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (words.Length == 0)
        {
            return "";
        }

        if (words.Length >= 2 && Kunya.Contains(words[0]))
        {
            return $" يا {words[0]} {words[1]}";
        }

        return person.NameGender switch
        {
            Person.Male => $" يا أستاذ {words[0]}",
            Person.Female => $" يا أستاذة {words[0]}",
            _ => "",
        };
    }
}
