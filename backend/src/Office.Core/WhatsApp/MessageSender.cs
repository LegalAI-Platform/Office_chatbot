using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Office.Core.WhatsApp;

public sealed record SendResult(bool Ok, Guid? OutboundId, string? Wamid, string Status, string? ErrorCode, string? ErrorMessage, bool Duplicate = false);

/// <summary>
/// Sending rules: validation, idempotency, the 24-hour window for free-form messages,
/// approved templates only, and recording the Graph API result.
/// </summary>
public sealed partial class MessageSender(IWhatsAppStore store, IWhatsAppCloudApi api, IOpsNotifier notifier, IWhatsAppSettings settings, TimeProvider clock)
{
    public const int MaxTextLength = 4096;

    [GeneratedRegex("^[0-9]{8,15}$")]
    private static partial Regex WaIdPattern();

    public async Task<SendResult> SendTextAsync(string waId, string text, string idempotencyKey, string requestedBy, string? contextWamid, CancellationToken ct)
    {
        Validate(waId, idempotencyKey, requestedBy);
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
        {
            throw new ArgumentException($"text must be 1-{MaxTextLength} characters", nameof(text));
        }

        var request = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = waId,
            ["type"] = "text",
            ["text"] = new JsonObject { ["preview_url"] = false, ["body"] = text },
        };
        if (contextWamid is not null)
        {
            request["context"] = new JsonObject { ["message_id"] = contextWamid };
        }

        return await SendAsync(waId, OutboundKinds.Text, null, null, request, idempotencyKey, requestedBy, ct);
    }

    public async Task<SendResult> SendTemplateAsync(string waId, string templateName, string language, JsonArray? components, string idempotencyKey, string requestedBy, CancellationToken ct)
    {
        Validate(waId, idempotencyKey, requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var template = new JsonObject
        {
            ["name"] = templateName,
            ["language"] = new JsonObject { ["code"] = language },
        };
        if (components is { Count: > 0 })
        {
            template["components"] = components.DeepClone();
        }

        var request = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = waId,
            ["type"] = "template",
            ["template"] = template,
        };

        return await SendAsync(waId, OutboundKinds.Template, templateName, language, request, idempotencyKey, requestedBy, ct);
    }

    private async Task<SendResult> SendAsync(string waId, string kind, string? templateName, string? language, JsonObject request,
        string idempotencyKey, string requestedBy, CancellationToken ct)
    {
        if (!settings.Enabled)
        {
            return new SendResult(false, null, null, OutboundStatuses.Failed, SendErrorCodes.Disabled, "WhatsApp is disabled");
        }

        var now = clock.GetUtcNow();
        var outbound = new OutboundMessage
        {
            Id = Guid.CreateVersion7(),
            IdempotencyKey = idempotencyKey,
            WaId = waId,
            Kind = kind,
            TemplateName = templateName,
            TemplateLanguage = language,
            RequestPayload = request.ToJsonString(),
            RequestedBy = requestedBy,
            Status = OutboundStatuses.Queued,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (!await store.TryAddOutboundAsync(outbound, ct))
        {
            var existing = await store.GetOutboundByKeyAsync(idempotencyKey, ct)
                ?? throw new InvalidOperationException("Outbound row vanished after an idempotency conflict.");
            return new SendResult(existing.Status is OutboundStatuses.Sent or OutboundStatuses.Delivered or OutboundStatuses.Read,
                existing.Id, existing.Wamid, existing.Status, null, null, Duplicate: true);
        }

        if (kind != OutboundKinds.Template)
        {
            var lastInbound = await store.GetLastInboundAtAsync(waId, ct);
            if (!ContactWindowPolicy.IsOpen(lastInbound, now))
            {
                outbound.Status = OutboundStatuses.BlockedWindow;
                outbound.LastError = new JsonObject { ["code"] = SendErrorCodes.OutsideWindow, ["last_inbound_at"] = lastInbound?.ToString("O") }.ToJsonString();
                await store.SaveOutboundAsync(outbound, ct);
                return new SendResult(false, outbound.Id, null, outbound.Status, SendErrorCodes.OutsideWindow, "Outside the 24-hour window; use a template");
            }
        }
        else if (!await store.IsTemplateApprovedAsync(templateName!, language!, ct))
        {
            outbound.Status = OutboundStatuses.Failed;
            outbound.LastError = new JsonObject { ["code"] = SendErrorCodes.TemplateNotApproved }.ToJsonString();
            await store.SaveOutboundAsync(outbound, ct);
            return new SendResult(false, outbound.Id, null, outbound.Status, SendErrorCodes.TemplateNotApproved, $"Template {templateName}/{language} is not approved");
        }

        outbound.Status = OutboundStatuses.Sending;
        outbound.Attempts++;
        await store.SaveOutboundAsync(outbound, ct);

        var result = await api.SendMessageAsync(outbound.RequestPayload, ct);
        outbound.ResponsePayload = result.ResponseJson;

        if (result.Ok && result.Wamid is not null)
        {
            outbound.Wamid = result.Wamid;
            outbound.Status = OutboundStatuses.Sent;
            outbound.SentAt = clock.GetUtcNow();
            outbound.LastError = null;
            await store.SaveOutboundAsync(outbound, ct);
            return new SendResult(true, outbound.Id, outbound.Wamid, outbound.Status, null, null);
        }

        var code = result.ErrorCode;
        outbound.Status = OutboundStatuses.Failed;
        outbound.LastError = new JsonObject
        {
            ["code"] = code,
            ["meta_code"] = result.MetaCode,
            ["message"] = result.ErrorMessage,
        }.ToJsonString();
        await store.SaveOutboundAsync(outbound, ct);

        if (code == SendErrorCodes.TokenInvalid)
        {
            await notifier.NotifyAsync("WhatsApp access token is invalid or expired (Meta error 190). Sending is failing.", ct);
        }

        return new SendResult(false, outbound.Id, null, outbound.Status, code, result.ErrorMessage);
    }

    private static void Validate(string waId, string idempotencyKey, string requestedBy)
    {
        if (waId is null || !WaIdPattern().IsMatch(waId))
        {
            throw new ArgumentException("wa_id must be 8-15 digits", nameof(waId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
    }
}
