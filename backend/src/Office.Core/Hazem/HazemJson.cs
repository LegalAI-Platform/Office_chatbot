using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Office.Core.Hazem;

/// <summary>JSON shape shared by the HTTP contract and the jsonb columns: snake_case, nulls left out.</summary>
public static class HazemJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions());

    public static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        o.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping; // keep Arabic readable in jsonb
        return o;
    }
}
