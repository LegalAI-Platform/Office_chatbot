using Office.Application.Texts;
using Office.Application.Understanding;
using Office.Domain.Clients;

namespace Office.Application.Conversations;

/// <summary>The person, conversation and current intake of one number (null for a number never seen).</summary>
public sealed record ClientState(Person Person, Conversation Conversation, Intake Intake);

public interface IConversationStore
{
    Task<ClientState?> LoadAsync(string waId, CancellationToken ct);
}

/// <summary>
/// A task for the office (TASK 04 placeholder for S-05): one row in hazem.pending_tasks, no staff,
/// no panel and no due time yet. Hazem never promises the client a contact time for it (B-06).
/// </summary>
public sealed record PendingTask(string Kind)
{
    public const string ExistingClientMessage = "existing_client_message";
    public const string HumanRequested = "human_requested";
    public const string UnclearMessage = "unclear_message";
    public const string UrgentRequest = "urgent_request";
    public const string ServiceRequest = "service_request";
    public const string RescheduleOrCancel = "reschedule_or_cancel";
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
    string? FallbackReason)
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
        c.FallbackReason);
}

/// <summary>Everything a Hazem turn changes, saved in the same transaction as the turn itself.</summary>
public sealed record TurnEffects(
    ClientState State,
    IReadOnlyList<PendingTask> Tasks,
    ClassificationRecord? Classification,
    IReadOnlyList<ReplyRecord> Replies);
