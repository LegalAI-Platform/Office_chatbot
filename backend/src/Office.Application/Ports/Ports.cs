using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Turns;

namespace Office.Application.Ports;

public interface ITurnStore
{
    Task<StoredTurn?> FindAsync(string turnId, CancellationToken ct);

    /// <summary>
    /// Inserts the turn and, in the same transaction, saves what it changed; false (and nothing
    /// saved) when a turn with the same turn_id already exists.
    /// </summary>
    Task<bool> TryAddAsync(StoredTurn turn, TurnEffects? effects, CancellationToken ct);
}

public interface ISettingsReader
{
    Task<HazemSettings> GetAsync(CancellationToken ct);
}

public interface ISendResultStore
{
    /// <summary>Inserts or updates one row per idempotency key; returns how many were written.</summary>
    Task<int> UpsertAsync(string? turnId, IReadOnlyList<SendResult> results, string reportedBy, CancellationToken ct);
}

/// <summary>Sends through n8n's internal webhook; n8n is the only part that talks to Meta.</summary>
public interface IWhatsAppGateway
{
    Task<SendResult> SendAsync(OutboundMessage message, CancellationToken ct);
}

public sealed record OutboundMessage(
    ActionType Type,
    string WaId,
    string IdempotencyKey,
    string RequestedBy,
    string? Text = null,
    string? TemplateName = null,
    string? Language = null,
    InteractivePayload? Interactive = null);

/// <summary>Outcome of one send, reported by n8n (POST /api/hazem/send-results) or by the API itself.</summary>
public sealed record SendResult(
    string? IdempotencyKey,
    bool Ok,
    string? OutboundId = null,
    string? Wamid = null,
    string? Status = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
