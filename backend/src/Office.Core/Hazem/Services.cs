namespace Office.Core.Hazem;

public interface ITurnStore
{
    Task<StoredTurn?> FindAsync(string turnId, CancellationToken ct);

    /// <summary>Inserts the turn; false when a turn with the same turn_id already exists.</summary>
    Task<bool> TryAddAsync(StoredTurn turn, CancellationToken ct);
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
    string Type,
    string WaId,
    string IdempotencyKey,
    string RequestedBy,
    string? Text = null,
    string? TemplateName = null,
    string? Language = null);

public sealed class TurnService(ITurnStore turns, ISettingsReader settings)
{
    public async Task<TurnResponse> HandleAsync(TurnRequest request, CancellationToken ct)
    {
        var turnId = request.TurnId!;
        if (await turns.FindAsync(turnId, ct) is { } existing)
        {
            return new TurnResponse(turnId, TurnResponse.Duplicate, existing.Actions);
        }

        var (responder, actions) = TurnResponder.Decide(request, await settings.GetAsync(ct));
        var stored = new StoredTurn(turnId, request.WaId!, request.ContactName, request.Messages!, TurnResponse.Processed, responder, actions);
        if (await turns.TryAddAsync(stored, ct))
        {
            return new TurnResponse(turnId, TurnResponse.Processed, actions);
        }

        // The same turn arrived twice at the same time: the other request stored it first.
        var winner = await turns.FindAsync(turnId, ct)
            ?? throw new InvalidOperationException($"turn {turnId} conflicted but was not found");
        return new TurnResponse(turnId, TurnResponse.Duplicate, winner.Actions);
    }
}

public sealed class OutboundService(IWhatsAppGateway gateway, ISendResultStore results)
{
    public const string ReportedByApi = "api";

    public async Task<SendResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        var result = await gateway.SendAsync(message, ct) with { IdempotencyKey = message.IdempotencyKey };
        await results.UpsertAsync(null, [result], ReportedByApi, ct);
        return result;
    }
}
