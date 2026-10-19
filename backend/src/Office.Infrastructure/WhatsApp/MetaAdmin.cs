using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Office.Core.WhatsApp;

namespace Office.Infrastructure.WhatsApp;

/// <summary>
/// Meta configuration calls used by Office.Cli and the template sync job:
/// webhook subscription, WABA subscription, phone number status, templates.
/// </summary>
public sealed class MetaAdmin(GraphApiClient graph, IWhatsAppStore store, IOptions<WhatsAppOptions> options, TimeProvider clock)
{
    private WhatsAppOptions O => options.Value;

    /// <summary>Registers (or updates) the app's webhook for whatsapp_business_account events.</summary>
    public async Task<JsonObject> SubscribeWebhookAsync(string callbackUrl, CancellationToken ct)
    {
        if (!callbackUrl.StartsWith("https://", StringComparison.Ordinal))
        {
            throw new ArgumentException("callback URL must be https", nameof(callbackUrl));
        }

        var response = await graph.PostFormAsync($"{O.AppId}/subscriptions", new Dictionary<string, string>
        {
            ["object"] = "whatsapp_business_account",
            ["callback_url"] = callbackUrl,
            ["verify_token"] = O.VerifyToken,
            ["fields"] = "messages",
            ["include_values"] = "true",
        }, appToken: true, ct);

        return Describe("subscribe", response);
    }

    public async Task<JsonObject> CheckAsync(CancellationToken ct)
    {
        var subscriptions = await graph.GetAsync($"{O.AppId}/subscriptions", appToken: true, ct);
        var wabaApps = await graph.GetAsync($"{O.WabaId}/subscribed_apps", ct: ct);
        var phone = await graph.GetAsync(
            $"{O.PhoneNumberId}?fields=display_phone_number,verified_name,quality_rating,code_verification_status,name_status", ct: ct);

        var waSubscription = (subscriptions.Body?["data"] as JsonArray)?
            .FirstOrDefault(s => s?["object"]?.GetValue<string>() == "whatsapp_business_account");

        return new JsonObject
        {
            ["app_subscription_ok"] = subscriptions.Ok,
            ["callback_url"] = waSubscription?["callback_url"]?.DeepClone(),
            ["active"] = waSubscription?["active"]?.DeepClone(),
            ["fields"] = waSubscription?["fields"]?.DeepClone(),
            ["waba_subscribed_apps_ok"] = wabaApps.Ok,
            ["waba_subscribed_apps"] = wabaApps.Body?["data"]?.DeepClone(),
            ["phone_number"] = phone.Ok ? phone.Body?.DeepClone() : null,
            ["errors"] = new JsonArray(new[] { subscriptions, wabaApps, phone }
                .Where(r => !r.Ok).Select(r => (JsonNode?)JsonValue.Create(r.MetaMessage ?? "error")).ToArray()),
        };
    }

    /// <summary>Makes sure the WABA delivers its events to this app.</summary>
    public async Task<JsonObject> SubscribeWabaAsync(CancellationToken ct) =>
        Describe("waba-subscribe", await graph.PostFormAsync($"{O.WabaId}/subscribed_apps", new Dictionary<string, string>(), ct: ct));

    /// <summary>Submits every catalog template Meta does not already have. Never resubmits or deletes.</summary>
    public async Task<JsonObject> SubmitTemplatesAsync(CancellationToken ct)
    {
        var existing = await ListTemplatesAsync(ct);
        var existingKeys = existing.Select(t => (t.Name, t.Language)).ToHashSet();
        var results = new JsonArray();

        foreach (var template in TemplatesCatalog.Load())
        {
            if (existingKeys.Contains((template.Name, template.Language)))
            {
                results.Add(new JsonObject { ["name"] = template.Name, ["result"] = "already_exists" });
                continue;
            }

            var response = await graph.PostJsonAsync($"{O.WabaId}/message_templates", template.ToCreateRequest(), ct: ct);
            results.Add(new JsonObject
            {
                ["name"] = template.Name,
                ["result"] = response.Ok ? "submitted" : "failed",
                ["status"] = response.Body?["status"]?.DeepClone(),
                ["category"] = response.Body?["category"]?.DeepClone(),
                ["error"] = response.Ok ? null : response.MetaMessage,
            });
        }

        await SyncTemplatesAsync(ct);
        return new JsonObject { ["templates"] = results };
    }

    /// <summary>Pulls every template from Meta into app.wa_templates. Flags category changes.</summary>
    public async Task<JsonObject> SyncTemplatesAsync(CancellationToken ct)
    {
        var templates = await ListTemplatesAsync(ct);
        var catalog = TemplatesCatalog.Load().ToDictionary(t => (t.Name, t.Language));
        var now = clock.GetUtcNow();
        var report = new JsonArray();

        foreach (var t in templates)
        {
            await store.UpsertTemplateAsync((t with { LastSyncedAt = now }).ToEntity(), ct);
            var categoryChanged = catalog.TryGetValue((t.Name, t.Language), out var wanted)
                && !string.Equals(wanted.Category, t.Category, StringComparison.OrdinalIgnoreCase);
            report.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["language"] = t.Language,
                ["status"] = t.Status,
                ["category"] = t.Category,
                ["category_changed_by_meta"] = categoryChanged,
                ["rejection_reason"] = t.RejectionReason,
            });
        }

        return new JsonObject { ["synced"] = templates.Count, ["templates"] = report };
    }

    private async Task<List<SyncedTemplate>> ListTemplatesAsync(CancellationToken ct)
    {
        var list = new List<SyncedTemplate>();
        var path = $"{O.WabaId}/message_templates?fields=name,language,status,category,id,components,rejected_reason&limit=100";
        while (path is not null)
        {
            var response = await graph.GetAsync(path, ct: ct);
            if (!response.Ok)
            {
                throw new InvalidOperationException($"Listing templates failed: {response.MetaMessage}");
            }

            foreach (var t in response.Body?["data"] as JsonArray ?? [])
            {
                list.Add(new SyncedTemplate(
                    t!["name"]!.GetValue<string>(),
                    t["language"]!.GetValue<string>(),
                    t["category"]?.GetValue<string>(),
                    t["status"]?.GetValue<string>(),
                    t["id"]?.GetValue<string>(),
                    t["components"]?.ToJsonString(),
                    t["rejected_reason"]?.GetValue<string>() is { } r && r != "NONE" ? r : null));
            }

            var after = response.Body?["paging"]?["cursors"]?["after"]?.GetValue<string>();
            path = response.Body?["paging"]?["next"] is null || after is null
                ? null
                : $"{O.WabaId}/message_templates?fields=name,language,status,category,id,components,rejected_reason&limit=100&after={Uri.EscapeDataString(after)}";
        }

        return list;
    }

    private static JsonObject Describe(string action, GraphResponse r) => new()
    {
        ["action"] = action,
        ["ok"] = r.Ok,
        ["http_status"] = r.Status is null ? null : (int)r.Status,
        ["meta_code"] = r.MetaCode,
        ["message"] = r.Ok ? null : r.MetaMessage,
        ["response"] = r.Ok ? r.Body?.DeepClone() : null,
    };

    private sealed record SyncedTemplate(string Name, string Language, string? Category, string? Status, string? MetaTemplateId, string? Components, string? RejectionReason)
    {
        public DateTimeOffset? LastSyncedAt { get; init; }

        public WhatsAppTemplate ToEntity() => new()
        {
            Name = Name,
            Language = Language,
            Category = Category,
            Status = Status,
            MetaTemplateId = MetaTemplateId,
            Components = Components,
            RejectionReason = RejectionReason,
            LastSyncedAt = LastSyncedAt,
        };
    }
}
