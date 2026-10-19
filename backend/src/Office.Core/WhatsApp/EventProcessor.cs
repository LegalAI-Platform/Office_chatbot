using System.Text.Json.Nodes;

namespace Office.Core.WhatsApp;

public sealed record EventProcessingResult(int MessagesNew, int MessagesDuplicate, int MessagesGroupIgnored, int StatusesNew, bool TemplateUpdated);

/// <summary>
/// Turns one stored webhook event into inbound messages, delivery statuses and template
/// updates. Idempotent: running it twice for the same event changes nothing.
/// </summary>
public sealed class EventProcessor(IWhatsAppStore store, TimeProvider clock)
{
    public async Task<EventProcessingResult?> ProcessAsync(Guid eventId, CancellationToken ct)
    {
        var evt = await store.GetWebhookEventAsync(eventId, ct);
        if (evt is null || evt.ProcessingStatus is WebhookEventStatuses.Processed or WebhookEventStatuses.Ignored)
        {
            return null;
        }

        var payload = JsonNode.Parse(evt.Payload);
        EventProcessingResult? result = null;

        await store.InTransactionAsync(async tx =>
        {
            result = await ApplyAsync(evt, payload, tx);
            var touched = result.MessagesNew + result.MessagesDuplicate + result.MessagesGroupIgnored > 0
                || (payload.Arr("statuses")?.Count ?? 0) > 0
                || result.TemplateUpdated;
            await store.MarkWebhookEventAsync(evt.Id,
                touched ? WebhookEventStatuses.Processed : WebhookEventStatuses.Ignored, null, clock.GetUtcNow(), tx);
        }, ct);

        return result;
    }

    private async Task<EventProcessingResult> ApplyAsync(WebhookEvent evt, JsonNode? payload, CancellationToken ct)
    {
        int msgNew = 0, msgDup = 0, msgGroup = 0, stNew = 0;
        var now = clock.GetUtcNow();
        var phoneNumberId = payload.Obj("metadata").Str("phone_number_id") ?? "";
        var contacts = payload.Arr("contacts") ?? [];

        foreach (var m in payload.Arr("messages") ?? [])
        {
            var waId = m.Str("from") ?? "";
            var wamid = m.Str("id");
            if (wamid is null || waId.Length == 0)
            {
                continue;
            }

            var isGroup = m.Has("group_id") || m.Obj("context").Has("group_id");
            var sentAt = m.UnixTime("timestamp") ?? now;

            var inserted = await store.TryAddInboundMessageAsync(new InboundMessage
            {
                Id = Guid.CreateVersion7(),
                Wamid = wamid,
                WaId = waId,
                ContactName = contacts.FirstOrDefault(c => c.Str("wa_id") == waId).Obj("profile").Str("name"),
                PhoneNumberId = phoneNumberId,
                MessageType = m.Str("type") ?? "unknown",
                SentAt = sentAt,
                ReceivedAt = now,
                ContextWamid = m.Obj("context").Str("id"),
                Referral = m.Obj("referral").Json(),
                IsGroup = isGroup,
                Raw = m!.Json() ?? "{}",
                WebhookEventId = evt.Id,
                ProcessingStatus = isGroup ? InboundStatuses.IgnoredGroup : InboundStatuses.Pending,
                UpdatedAt = now,
            }, ct);

            if (!inserted)
            {
                msgDup++;
            }
            else if (isGroup)
            {
                msgGroup++; // No window: the office never replies into groups.
            }
            else
            {
                msgNew++;
                await store.OpenContactWindowAsync(waId, sentAt, wamid, ct);
            }
        }

        foreach (var s in payload.Arr("statuses") ?? [])
        {
            var wamid = s.Str("id");
            var status = s.Str("status");
            if (wamid is null || status is null)
            {
                continue;
            }

            if (await store.TryAddMessageStatusAsync(new MessageStatus
            {
                Id = Guid.CreateVersion7(),
                Wamid = wamid,
                Status = status,
                StatusAt = s.UnixTime("timestamp") ?? now,
                RecipientId = s.Str("recipient_id"),
                Errors = s.Arr("errors").Json(),
                Pricing = s.Obj("pricing").Json(),
                Raw = s!.Json() ?? "{}",
                ReceivedAt = now,
            }, ct))
            {
                stNew++;
            }

            await store.AdvanceOutboundStatusAsync(wamid, status, s.Arr("errors").Json(), ct);
        }

        var templateUpdated = false;
        if (evt.Field == "message_template_status_update"
            && payload.Str("message_template_name") is { } name
            && payload.Str("message_template_language") is { } language)
        {
            var reason = payload.Str("reason");
            await store.UpsertTemplateStatusAsync(name, language, payload.Str("event"),
                payload?["message_template_id"]?.ToString(), reason == "NONE" ? null : reason, now, ct);
            templateUpdated = true;
        }

        return new EventProcessingResult(msgNew, msgDup, msgGroup, stNew, templateUpdated);
    }
}
