using Office.Application.Texts;
using Office.Application.Understanding;
using Office.Domain.Clients;

namespace Office.Application.Conversations;

/// <summary>
/// The person, conversation and current intake of one number (null for a number never seen).
/// <see cref="ClosedIntakes"/>: requests closed during this turn (B-04: an existing client's new matter).
/// </summary>
public sealed record ClientState(Person Person, Conversation Conversation, Intake Intake)
{
    public List<Intake> ClosedIntakes { get; init; } = [];
}

public interface IConversationStore
{
    Task<ClientState?> LoadAsync(string waId, CancellationToken ct);

    /// <summary>Saves the conversation alone (staff events, unmute), outside a turn.</summary>
    Task SaveConversationAsync(Conversation conversation, CancellationToken ct);
}

/// <summary>B-14: where a number is on the office lists (several at once is possible).</summary>
public sealed record ListMatch(bool Blocked, bool LawyerDirect, ExistingClientEntry? ExistingClient)
{
    public static ListMatch None { get; } = new(false, false, null);
}

/// <summary>B-04: an existing client and the staff member in charge (both from the office's file).</summary>
public sealed record ExistingClientEntry(string? Name, Guid? StaffId);

public interface IClientLists
{
    Task<ListMatch> FindAsync(string waId, CancellationToken ct);
}

/// <summary>
/// A staff task (S-05; hazem.staff_tasks). <see cref="DueAt"/> follows B-06. Nobody is alerted
/// until the panel (TASK 07), so Hazem never promises the client a contact time for it.
/// </summary>
public sealed record PendingTask(string Kind, DateTimeOffset DueAt, Guid? StaffId = null)
{
    // S-05 types
    public const string ExistingClientMessage = "existing_client_message";
    public const string HumanRequested = "human_requested";
    public const string Complaint = "complaint";

    // TASK 04 / 05a additions (documented in the reports)
    public const string UnclearMessage = "unclear_message";
    public const string UrgentRequest = "urgent_request";
    public const string ServiceRequest = "service_request";
    public const string RescheduleOrCancel = "reschedule_or_cancel";
    public const string LawyerDirectMessage = "lawyer_direct_message";
    public const string ExternalContact = "external_contact";

    // TASK 08 (booking): S-05 types used by the booking, and the booking's own (documented in the report).
    public const string LateReschedule = "late_reschedule";
    public const string Refund = "refund";
    public const string BookingUnavailable = "booking_unavailable";
    public const string BookingCancelled = "booking_cancelled";
    public const string BookingMessageFailed = "booking_message_failed";
    public const string CalendarConflict = "calendar_conflict";
    public const string OfficeCancelled = "office_cancelled"; // the office cancelled while booking from WhatsApp is off: the staff arrange
    public const string ServicePricing = "service_pricing";   // TASK 10: an order's documents came, the lawyer or the manager prices it
    public const string ServiceUnpaid = "service_unpaid";     // TASK 10: the price ran out unpaid, the order closed
    public const string UrfReview = "urf_review";             // TASK 11: an Urfi case's documents came, the lawyer reviews them
    public const string UrfDocumentsMissing = "urf_documents_missing"; // TASK 11: 24 hours left, no document yet
    public const string UrfHelp = "urf_help";                 // TASK 11: the ages could not be read twice
    public const string OfficeInfoMissing = "office_info_missing"; // TASK 06: an office question whose answer is not written in the panel yet
    public const string ServiceQuestion = "service_question"; // v1.22: does the office do one specific thing? the office answers
    public const string ReminderFailed = "reminder_failed";   // TASK 12: a reminder could not be sent, the office reminds by phone
    public const string PaymentReviewDue = "payment_review_due"; // 1.25 (B-08): the appointment is soon and its transfer is not reviewed
    public const string RatingLow = "rating_low";             // 1.31 (B-26): «يحتاج للتحسين», the managers call the client
    public const string UrgentCallFailed = "urgent_call_failed"; // 1.33 (B-09): the lawyer's alert could not go (no number, a refused send)
    public const string UrgentCallUnanswered = "urgent_call_unanswered"; // 1.33 (B-09): «هكلمه» not pressed in time: the managers
}

/// <summary>hazem.classifications: no client text and no name, only what the AI decided and what it cost.</summary>
public sealed record ClassificationRecord(
    string TurnId,
    string Intent,
    double Confidence,
    string? Service,
    string? ConsultationMode,
    bool FullNamePresent,
    string? Model,
    string? PromptVersion,
    int? LatencyMs,
    int? InputTokens,
    int? OutputTokens,
    string? FallbackReason,
    string? MatterType = null,
    string? MatterSubtype = null,
    string? SenderRole = null,
    string? InfoTopic = null,
    string? Scope = null)
{
    public static ClassificationRecord From(string turnId, Classification c) => new(
        turnId,
        Classification.Code(c.Intent),
        c.Confidence,
        c.Service is { } s ? Classification.Code(s) : null,
        Classification.Code(c.Mode),
        c.FullName is not null,
        c.Call?.Model,
        c.Call?.PromptVersion,
        c.Call?.LatencyMs,
        c.Call?.InputTokens,
        c.Call?.OutputTokens,
        c.LoggedReason,
        c.Matter is { } m ? Classification.Code(m) : null,
        c.Subtype is { } st ? Classification.Code(st) : null,
        Classification.Code(c.Role),
        c.Topic is { } t ? Classification.Code(t) : null,
        c.Call?.Answer?.Scope); // what the model said (the guard turns outside into off_topic)
}

/// <summary>Everything a Hazem turn changes, saved in the same transaction as the turn itself.</summary>
public sealed record TurnEffects(
    ClientState State,
    IReadOnlyList<PendingTask> Tasks,
    ClassificationRecord? Classification,
    IReadOnlyList<ReplyRecord> Replies,
    Writing.WrittenReply? Written = null); // v1.41 (TASK 17): the everyday reply in the writer's words, or why not
