using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Office.Application.Responders;
using Office.Application.Understanding;

namespace Office.Infrastructure.Ai;

/// <summary>
/// T-07/T-08: OpenAI Chat Completions with Structured Outputs (strict JSON schema), temperature 0.
/// Receives text only (built by AiInput, T-09). Never throws for an AI failure: the outcome says why.
/// The key (HAZEM_OPENAI_API_KEY) is never logged; this HttpClient has no loggers.
/// </summary>
public sealed class OpenAiIntentClassifier(HttpClient http, IOptions<HazemOptions> options, IHostEnvironment env, TimeProvider clock)
    : IIntentClassifier
{
    public const string HttpClientName = "openai";
    /// <summary>The prompt in use: Ai/prompts/{PromptVersion}.md. Older versions stay in the folder as history.</summary>
    public const string PromptVersion = "classify-v10";
    public const string StubEnabledKey = "ai.stub_enabled";
    public const string StubBaseUrlKey = "ai.stub_base_url";

    /// <summary>Sent to the test stub instead of any real key (hazem-e2e).</summary>
    public const string StubKey = "e2e-stub-not-a-real-key";

    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(15);

    /// <summary>Total time for the call and its one retry (tests shorten it).</summary>
    public TimeSpan Budget { get; init; } = DefaultBudget;

    private static readonly Lazy<string> Prompt = new(() =>
    {
        using var s = typeof(OpenAiIntentClassifier).Assembly.GetManifestResourceStream($"Office.Infrastructure.Ai.prompts.{PromptVersion}.md")
            ?? throw new InvalidOperationException($"embedded prompt {PromptVersion}.md is missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    });

    public static JsonObject Schema { get; } = BuildSchema();

    /// <summary>The prompt in use, for both providers (v1.26).</summary>
    public static string PromptText => Prompt.Value;

    public async Task<ClassifierOutcome> ClassifyAsync(string text, HazemSettings settings, CancellationToken ct)
    {
        var o = options.Value;
        var (baseUrl, key) = UseStub(settings) ? (settings.Get(StubBaseUrlKey), StubKey) : (o.OpenAiBaseUrl, o.OpenAiApiKey);
        var started = clock.GetTimestamp();
        ClassifierOutcome Done(RawClassification? answer, string? failure, int? input = null, int? output = null) =>
            new(answer, failure, o.OpenAiModel, PromptVersion, (int)clock.GetElapsedTime(started).TotalMilliseconds, input, output);

        if (string.IsNullOrWhiteSpace(key))
        {
            return Done(null, ClassifierOutcome.NoKey); // classification switched off: Hazem answers "unclear"
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions")
                {
                    Content = JsonContent.Create(Body(o.OpenAiModel, text)),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = await http.SendAsync(request, budget.Token);
                if (IsTransient(response.StatusCode) && attempt == 1)
                {
                    continue; // one retry for a temporary error
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Done(null, ClassifierOutcome.Error);
                }

                var body = await response.Content.ReadFromJsonAsync<JsonNode>(budget.Token);
                var usage = body?["usage"];
                var (answer, failure) = Parse(body?["choices"]?[0]?["message"]?["content"]?.GetValue<string>());
                return Done(answer, failure, (int?)usage?["prompt_tokens"], (int?)usage?["completion_tokens"]);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Done(null, ClassifierOutcome.Timeout);
            }
            catch (HttpRequestException) when (attempt == 1)
            {
                // one retry for a network error
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException)
            {
                return Done(null, ex is HttpRequestException ? ClassifierOutcome.Error : ClassifierOutcome.InvalidOutput);
            }
        }
    }

    /// <summary>The content must be the schema's object; anything else is invalid output (checked again in Application).</summary>
    public static (RawClassification? Answer, string? Failure) Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, ClassifierOutcome.InvalidOutput);
        }

        try
        {
            if (JsonNode.Parse(content) is not JsonObject o
                || o["intent"] is not JsonValue intent || intent.GetValueKind() != JsonValueKind.String
                || o["confidence"] is not JsonValue confidence || confidence.GetValueKind() != JsonValueKind.Number)
            {
                return (null, ClassifierOutcome.InvalidOutput);
            }

            return (new RawClassification(
                intent.GetValue<string>(),
                StringOrNull(o["service"]),
                StringOrNull(o["consultation_mode"]),
                StringOrNull(o["full_name"]),
                confidence.GetValue<double>(),
                StringOrNull(o["matter_type"]),
                StringOrNull(o["matter_subtype"]),
                StringOrNull(o["sender_role"]),
                StringOrNull(o["info_topic"]),
                StringOrNull(o["scope"])), null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return (null, ClassifierOutcome.InvalidOutput);
        }
    }

    /// <summary>The stub is honoured only in the test environment (ENVIRONMENT=test), never in production.</summary>
    private bool UseStub(HazemSettings settings) =>
        settings.Flag(StubEnabledKey) && env.IsEnvironment("test") && settings.Get(StubBaseUrlKey).Length > 0;

    private static bool IsTransient(HttpStatusCode code) =>
        code is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)code >= 500;

    private static string? StringOrNull(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static JsonObject Body(string model, string text) => new()
    {
        ["model"] = model,
        ["temperature"] = 0,
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = Prompt.Value },
            new JsonObject { ["role"] = "user", ["content"] = text }),
        ["response_format"] = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject { ["name"] = "hazem_intent", ["strict"] = true, ["schema"] = Schema.DeepClone() },
        },
    };

    private static JsonObject BuildSchema()
    {
        static JsonArray Codes<T>(bool nullable)
            where T : struct, Enum
        {
            var a = new JsonArray();
            foreach (var v in Enum.GetValues<T>())
            {
                a.Add(Classification.Code(v));
            }

            if (nullable)
            {
                a.Add(null);
            }

            return a;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("subject", "scope", "intent", "service", "consultation_mode", "full_name", "confidence",
                "matter_type", "matter_subtype", "sender_role", "info_topic"),
            // v1.24: subject and scope come first, so the model names what the turn is about before it picks an
            // intent. The subject is a few words for that only: it is never read, kept or logged.
            ["properties"] = new JsonObject
            {
                ["subject"] = new JsonObject { ["type"] = "string" },
                ["scope"] = new JsonObject { ["type"] = "string", ["enum"] = Codes<Scope>(false) },
                ["intent"] = new JsonObject { ["type"] = "string", ["enum"] = Codes<Intent>(false) },
                ["service"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["enum"] = Codes<Service>(true) },
                ["consultation_mode"] = new JsonObject { ["type"] = "string", ["enum"] = Codes<ConsultationMode>(false) },
                ["full_name"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["matter_type"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["enum"] = Codes<MatterType>(true) },
                ["matter_subtype"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["enum"] = Codes<MatterSubtype>(true) },
                ["sender_role"] = new JsonObject { ["type"] = "string", ["enum"] = Codes<SenderRole>(false) },
                ["info_topic"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["enum"] = Codes<InfoTopic>(true) },
                ["confidence"] = new JsonObject { ["type"] = "number" },
            },
        };
    }
}
