using Office.Application.Ports;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.UseCases;

namespace Office.Infrastructure.Booking;

/// <summary>
/// The workers' one way to tell a client something (the booking's and the service orders' notices): an approved
/// text only, through n8n. False when the text is not active, still has an empty slot, or n8n did not send it
/// (outside the 24-hour window too): the caller opens a task.
/// </summary>
public sealed class NoticeSender(SendOutbound send)
{
    public async Task<bool> SendAsync(string waId, string id, string requestedBy, ReplyStep step,
        IReadOnlyDictionary<string, ApprovedText> active, CancellationToken ct, DocumentPayload? document = null)
    {
        var composed = ReplyComposer.Compose(id, null, [step], active);
        var told = composed.Actions.Count > 0;
        foreach (var action in composed.Actions)
        {
            var r = await send.SendAsync(document is null
                ? new OutboundMessage(action.Type, waId, action.IdempotencyKey, requestedBy, action.Text, Interactive: action.Interactive)
                : new OutboundMessage(ActionType.SendDocument, waId, action.IdempotencyKey, requestedBy, action.Text, Document: document), ct);
            told &= r.Ok;
        }

        return told;
    }
}
