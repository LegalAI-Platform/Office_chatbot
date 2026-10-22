using Office.Application.Ports;
using Office.Application.Responders;
using Office.Application.Turns;

namespace Office.Application.UseCases;

/// <summary>POST /api/hazem/turns: one answer per turn_id, whatever happens.</summary>
public sealed class HandleTurn(ITurnStore turns, ISettingsReader settings, IResponderSelector responders)
{
    public async Task<TurnResult> HandleAsync(Turn turn, CancellationToken ct)
    {
        if (await turns.FindAsync(turn.TurnId, ct) is { } existing)
        {
            return new TurnResult(turn.TurnId, TurnStatus.Duplicate, existing.Actions);
        }

        var current = await settings.GetAsync(ct);
        var decision = await responders.Select(current).DecideAsync(turn, current, ct);
        var stored = new StoredTurn(turn.TurnId, turn.WaId, turn.ContactName, turn.Messages, decision.Responder, decision.Actions);
        if (await turns.TryAddAsync(stored, decision.Effects, ct))
        {
            return new TurnResult(turn.TurnId, TurnStatus.Processed, decision.Actions, decision.Notes);
        }

        // The same turn arrived twice at the same time: the other request stored it (and its
        // effects) first; this one's effects are dropped.
        var winner = await turns.FindAsync(turn.TurnId, ct)
            ?? throw new InvalidOperationException($"turn {turn.TurnId} conflicted but was not found");
        return new TurnResult(turn.TurnId, TurnStatus.Duplicate, winner.Actions);
    }
}

/// <summary>POST /api/hazem/send-results: what n8n did with each action.</summary>
public sealed class RecordSendResults(ISendResultStore results)
{
    public const string ReportedByN8n = "n8n";

    public Task<int> RecordAsync(string? turnId, IReadOnlyList<SendResult> sent, CancellationToken ct) =>
        results.UpsertAsync(turnId, sent, ReportedByN8n, ct);
}

/// <summary>A send the API starts itself (CLI today; reminders and the panel later).</summary>
public sealed class SendOutbound(IWhatsAppGateway gateway, ISendResultStore results)
{
    public const string ReportedByApi = "api";

    public async Task<SendResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        var result = await gateway.SendAsync(message, ct) with { IdempotencyKey = message.IdempotencyKey };
        await results.UpsertAsync(null, [result], ReportedByApi, ct);
        return result;
    }
}

/// <summary>hazem.settings "responder": hazem (default) or echo (tests of the old TASK 02/03 loop).</summary>
public sealed class ResponderSelector(IEnumerable<ITurnResponder> responders) : IResponderSelector
{
    public const string SettingKey = "responder";

    public ITurnResponder Select(HazemSettings settings)
    {
        var wanted = settings.Get(SettingKey, HazemResponder.ResponderName);
        return responders.FirstOrDefault(r => r.Name == wanted)
            ?? responders.First(r => r.Name == HazemResponder.ResponderName);
    }
}
