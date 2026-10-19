using System.Text.Json.Nodes;

namespace Office.Infrastructure.WhatsApp;

/// <summary>The M-03 templates kept in WhatsApp/Templates/whatsapp-templates.json (embedded resource).</summary>
public static class TemplatesCatalog
{
    private const string ResourceName = "Office.Infrastructure.WhatsApp.Templates.whatsapp-templates.json";

    public static IReadOnlyList<CatalogTemplate> Load()
    {
        using var stream = typeof(TemplatesCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        var root = JsonNode.Parse(stream) ?? throw new InvalidOperationException("Templates file is empty.");

        return (root["templates"] as JsonArray ?? [])
            .Select(t => new CatalogTemplate(
                t!["name"]!.GetValue<string>(),
                t["language"]!.GetValue<string>(),
                t["category"]!.GetValue<string>(),
                (JsonArray)t["components"]!))
            .ToList();
    }
}

public sealed record CatalogTemplate(string Name, string Language, string Category, JsonArray Components)
{
    /// <summary>Body for POST /{waba-id}/message_templates (only the fields Meta accepts).</summary>
    public JsonObject ToCreateRequest() => new()
    {
        ["name"] = Name,
        ["language"] = Language,
        ["category"] = Category,
        ["components"] = Components.DeepClone(),
    };
}
