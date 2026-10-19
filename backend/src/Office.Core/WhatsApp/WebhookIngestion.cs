using System.Text.Json;
using System.Text.Json.Nodes;

namespace Office.Core.WhatsApp;

public enum IngestionOutcome
{
    Accepted,
    InvalidSignature,
    NotWhatsApp,
    InvalidJson,
}

public sealed record IngestionResult(IngestionOutcome Outcome, IReadOnlyList<Guid> NewEventIds);

/// <summary>
/// Webhook POST handling that must finish fast: verify the signature, split the
/// envelope into one event per change, and store new events. Processing happens later.
/// </summary>
public sealed class WebhookIngestion(IWhatsAppStore store, TimeProvider clock)
{
    public async Task<IngestionResult> IngestAsync(byte[] rawBody, string? signatureHeader, string appSecret, CancellationToken ct)
    {
        if (!WebhookSignature.IsValid(rawBody, signatureHeader, appSecret))
        {
            return new IngestionResult(IngestionOutcome.InvalidSignature, []);
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawBody);
        }
        catch (JsonException)
        {
            return new IngestionResult(IngestionOutcome.InvalidJson, []);
        }

        if (root.Str("object") != "whatsapp_business_account")
        {
            return new IngestionResult(IngestionOutcome.NotWhatsApp, []);
        }

        var newIds = new List<Guid>();
        foreach (var entry in root.Arr("entry") ?? [])
        {
            foreach (var change in entry.Arr("changes") ?? [])
            {
                if (change.Obj("value") is not { } value)
                {
                    continue;
                }

                // Stored shape: the change value plus its field (same shape as the test fixtures).
                var evt = (JsonObject)value.DeepClone();
                evt["field"] = change.Str("field") ?? "messages";
                var canonical = JsonCanonicalizer.Canonicalize(evt);

                var id = await store.TryAddWebhookEventAsync(new WebhookEvent
                {
                    Id = Guid.CreateVersion7(),
                    ReceivedAt = clock.GetUtcNow(),
                    Field = evt.Str("field"),
                    PhoneNumberId = evt.Obj("metadata").Str("phone_number_id"),
                    Payload = canonical,
                    PayloadSha256 = JsonCanonicalizer.Sha256Hex(canonical),
                    SignatureValid = true,
                }, ct);

                if (id is { } newId)
                {
                    newIds.Add(newId);
                }
            }
        }

        return new IngestionResult(IngestionOutcome.Accepted, newIds);
    }
}
