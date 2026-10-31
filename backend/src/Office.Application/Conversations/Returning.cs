using Office.Application.Responders;
using Office.Domain.Clients;

namespace Office.Application.Conversations;

// v1.31 (DECISIONS B-01.7, S-02): a returning client. A known person whose request is finished starts a new one: the
// old intake is closed (nothing is deleted: bookings, orders, payments and the history stay), the name is kept, and
// Hazem says «أهلًا بحضرتك تاني يا …» before the services list, as a new client hears after giving his name (B-01.3).

/// <summary>What the person has with the office, as <see cref="ReturningClient"/> reads it.</summary>
public sealed record RequestItem(string Kind, string State, bool OfThisIntake, DateTimeOffset? StartsAt = null)
{
    public const string Appointment = "appointment"; // S-03
    public const string Order = "order";             // S-07
    public const string Payment = "payment";         // S-04 (of an appointment or an order)
    public const string Urf = "urf";                 // S-06
}

/// <summary>
/// The person's requests (<see cref="Items"/>: <see cref="RequestItem.OfThisIntake"/> marks the ones made under the open
/// intake) and how long he has been silent before this turn (<see cref="SilentFor"/>, null: he never wrote). The silence
/// is measured by the database's own clock, the one that stamped his messages.
/// </summary>
public sealed record IntakeFacts(TimeSpan? SilentFor, IReadOnlyList<RequestItem> Items);

public interface IIntakeHistory
{
    Task<IntakeFacts> FactsAsync(Guid personId, string waId, Guid intakeId, CancellationToken ct);

    /// <summary>v1.39 (TASK 16): the client's situation before this turn (nothing known: no situation).</summary>
    Task<SituationFacts> SituationAsync(Guid personId, string waId, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(SituationFacts.Empty);
}

/// <summary>B-01.7's rules. Pure: the flow and the tests use the same ones.</summary>
public static class ReturningClient
{
    /// <summary>Silent this many days with nothing pending: a new request (hazem.settings, the owner's 7).</summary>
    public const string FreshAfterDaysKey = "intake.fresh_after_days";

    public const int DefaultFreshAfterDays = 7;

    public static TimeSpan Quiet(HazemSettings s) =>
        TimeSpan.FromDays(int.TryParse(s.Get(FreshAfterDaysKey), out var d) && d is >= 1 and <= 365 ? d : DefaultFreshAfterDays);

    /// <summary>
    /// The open request is finished when either:
    /// (1) its work ended (an appointment completed, no-show, cancelled by either side or expired; an order delivered or
    ///     closed; an Urfi case refused by the lawyer) and nothing of the person is still open; or
    /// (2) the person has been silent <paramref name="quiet"/> and nothing is pending (no held or future appointment, no
    ///     order priced, waiting for its price or in progress, no receipt waiting for the office, no Urfi case running).
    /// </summary>
    public static bool Finished(IntakeFacts facts, DateTimeOffset now, TimeSpan quiet)
    {
        if (facts.Items.Any(i => i.OfThisIntake && Ended(i)) && !facts.Items.Any(Open))
        {
            return true;
        }

        return facts.SilentFor >= quiet && !facts.Items.Any(i => Pending(i, now));
    }

    /// <summary>The first word of the stored name ({{first_name}} in welcome_back); null without a name.</summary>
    public static string? FirstName(Person person) =>
        person.Name?.Raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    /// <summary>The request's work is over (rule 1). An Urfi contract ends with its appointment completed.</summary>
    private static bool Ended(RequestItem i) => i.Kind switch
    {
        RequestItem.Appointment => i.State is "completed" or "no_show" or "cancelled_by_client" or "cancelled_by_office" or "expired",
        RequestItem.Order => i.State is "delivered" or "closed",
        RequestItem.Urf => i.State == "lawyer_rejected",
        _ => false,
    };

    /// <summary>Still open (rule 1): anything not finished, a confirmed appointment until the office marks it.</summary>
    private static bool Open(RequestItem i) => i.Kind switch
    {
        RequestItem.Appointment => i.State is "pending_payment" or "confirmed",
        RequestItem.Order => i.State is "collecting" or "awaiting_price" or "awaiting_payment" or "in_progress",
        RequestItem.Payment => i.State is "proof_received" or "duplicate_suspected",
        RequestItem.Urf => i.State is "collecting" or "documents_pending" or "booked",
        _ => false,
    };

    /// <summary>Pending (rule 2): what still waits on the client or the office; an order left waiting for documents does not.</summary>
    private static bool Pending(RequestItem i, DateTimeOffset now) => i.Kind switch
    {
        RequestItem.Appointment => i.State == "pending_payment" || (i.State == "confirmed" && i.StartsAt > now),
        RequestItem.Order => i.State is "awaiting_price" or "awaiting_payment" or "in_progress",
        _ => Open(i),
    };
}
