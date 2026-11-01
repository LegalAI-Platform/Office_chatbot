using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using Office.Application.Responders;
using Office.Application.Writing;

namespace Office.Infrastructure.Ai;

/// <summary>
/// v1.41 (TASK 17): the writer's prompt (Ai/prompts/{PromptVersion}.md) and what it is sent: the purpose, the approved text
/// (its facts the only ones it may use), how to call the client, and the conversation. The answer is {"reply": "…"}.
/// </summary>
public static class WriterPrompt
{
    public const string PromptVersion = "write-v1";

    /// <summary>The whole call within this time (a written reply is never worth a late answer: the approved text goes).</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(6);

    private static readonly Lazy<string> Prompt = new(() =>
    {
        using var s = typeof(WriterPrompt).Assembly.GetManifestResourceStream($"Office.Infrastructure.Ai.prompts.{PromptVersion}.md")
            ?? throw new InvalidOperationException($"embedded prompt {PromptVersion}.md is missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    });

    public static string Text => Prompt.Value;

    public static JsonObject Schema { get; } = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("reply"),
        ["properties"] = new JsonObject { ["reply"] = new JsonObject { ["type"] = "string" } },
    };

    /// <summary>The user message: everything the writer may know for this one reply.</summary>
    public static string Message(WriteRequest r)
    {
        var sb = new StringBuilder();
        sb.Append("PURPOSE: ").Append(r.Purpose).Append('\n');
        sb.Append("APPROVED TEXT (keep its meaning; its facts are the only facts you may use):\n").Append(r.Approved).Append('\n');
        sb.Append("CALL HIM: ").Append(r.Call.Length > 0 ? r.Call : "(no name: say «حضرتك»)").Append('\n');
        sb.Append("CONVERSATION (oldest first; the last line is his message you answer now):\n");
        foreach (var line in r.History)
        {
            sb.Append(line.FromClient ? "العميل: " : "حازم: ").Append(line.Text.Replace('\n', ' ')).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>The reply from the schema's object; null when the content is anything else.</summary>
    public static string? Parse(string? content)
    {
        try
        {
            return content is not null && JsonNode.Parse(content) is JsonObject o && o["reply"] is JsonValue v
                                       && v.GetValueKind() == JsonValueKind.String
                ? v.GetValue<string>()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>v1.41: the writer through Anthropic (the classifier's key), its output held to the schema.</summary>
public sealed class AnthropicReplyWriter(IOptions<HazemOptions> options, TimeProvider clock) : IReplyWriter
{
    private const int MaxTokens = 1024;
    private AnthropicClient? _client;
    private string? _clientKey;

    public async Task<WriterOutcome> WriteAsync(WriteRequest request, HazemSettings settings, CancellationToken ct)
    {
        var o = options.Value;
        var model = settings.Get(Conversational.ModelKey) is { Length: > 0 } m && m.StartsWith("claude", StringComparison.Ordinal) ? m : o.AnthropicModel;
        var started = clock.GetTimestamp();
        WriterOutcome Done(string? reply, string? failure, int? input = null, int? output = null) =>
            new(reply, failure, model, WriterPrompt.PromptVersion, (int)clock.GetElapsedTime(started).TotalMilliseconds, input, output);

        if (string.IsNullOrWhiteSpace(o.AnthropicApiKey))
        {
            return Done(null, WriterOutcome.NoKey);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(WriterPrompt.Budget);
        try
        {
            if (_client is null || _clientKey != o.AnthropicApiKey)
            {
                _client = new AnthropicClient { ApiKey = o.AnthropicApiKey, MaxRetries = 0 };
                _clientKey = o.AnthropicApiKey;
            }

            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = model,
                MaxTokens = MaxTokens,
                System = new List<TextBlockParam> { new() { Text = WriterPrompt.Text, CacheControl = new CacheControlEphemeral() } },
                Messages = [new() { Role = Role.User, Content = WriterPrompt.Message(request) }],
                OutputConfig = new OutputConfig
                {
                    Effort = Effort.Low,
                    Format = new JsonOutputFormat
                    {
                        Schema = WriterPrompt.Schema.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value)),
                    },
                },
            }, budget.Token);
            var usage = response.Usage;
            var input = (int)(usage.InputTokens + (usage.CacheReadInputTokens ?? 0) + (usage.CacheCreationInputTokens ?? 0));
            if (response.StopReason != "end_turn")
            {
                return Done(null, WriterOutcome.InvalidOutput, input, (int)usage.OutputTokens);
            }

            var reply = WriterPrompt.Parse(response.Content.Select(b => b.Value).OfType<TextBlock>().LastOrDefault()?.Text);
            return Done(reply, reply is null ? WriterOutcome.InvalidOutput : null, input, (int)usage.OutputTokens);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Done(null, WriterOutcome.Timeout);
        }
        catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException or JsonException or InvalidOperationException)
        {
            return Done(null, WriterOutcome.Error);
        }
    }
}

/// <summary>v1.41: the writer through OpenAI Chat Completions (Structured Outputs), the classifier's key and client.</summary>
public sealed class OpenAiReplyWriter(IHttpClientFactory http, IOptions<HazemOptions> options, TimeProvider clock) : IReplyWriter
{
    public async Task<WriterOutcome> WriteAsync(WriteRequest request, HazemSettings settings, CancellationToken ct)
    {
        var o = options.Value;
        var model = settings.Get(Conversational.ModelKey) is { Length: > 0 } m && !m.StartsWith("claude", StringComparison.Ordinal) ? m : o.OpenAiModel;
        var started = clock.GetTimestamp();
        WriterOutcome Done(string? reply, string? failure, int? input = null, int? output = null) =>
            new(reply, failure, model, WriterPrompt.PromptVersion, (int)clock.GetElapsedTime(started).TotalMilliseconds, input, output);

        if (string.IsNullOrWhiteSpace(o.OpenAiApiKey))
        {
            return Done(null, WriterOutcome.NoKey);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(WriterPrompt.Budget);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, o.OpenAiBaseUrl.TrimEnd('/') + "/chat/completions")
            {
                Content = JsonContent.Create(new JsonObject
                {
                    ["model"] = model,
                    ["messages"] = new JsonArray(
                        new JsonObject { ["role"] = "system", ["content"] = WriterPrompt.Text },
                        new JsonObject { ["role"] = "user", ["content"] = WriterPrompt.Message(request) }),
                    ["response_format"] = new JsonObject
                    {
                        ["type"] = "json_schema",
                        ["json_schema"] = new JsonObject { ["name"] = "hazem_reply", ["strict"] = true, ["schema"] = WriterPrompt.Schema.DeepClone() },
                    },
                }),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.OpenAiApiKey);
            using var response = await http.CreateClient(OpenAiIntentClassifier.HttpClientName).SendAsync(message, budget.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Done(null, WriterOutcome.Error);
            }

            var body = await response.Content.ReadFromJsonAsync<JsonNode>(budget.Token);
            var reply = WriterPrompt.Parse(body?["choices"]?[0]?["message"]?["content"]?.GetValue<string>());
            return Done(reply, reply is null ? WriterOutcome.InvalidOutput : null, (int?)body?["usage"]?["prompt_tokens"],
                (int?)body?["usage"]?["completion_tokens"]);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Done(null, WriterOutcome.Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException)
        {
            return Done(null, WriterOutcome.Error);
        }
    }
}

/// <summary>v1.41: ai.provider picks who writes first, as for the classifier; no answer → the other one (never after a timeout).</summary>
public sealed class RoutingReplyWriter(OpenAiReplyWriter openAi, AnthropicReplyWriter anthropic) : IReplyWriter
{
    public async Task<WriterOutcome> WriteAsync(WriteRequest request, HazemSettings settings, CancellationToken ct)
    {
        var anthropicFirst = settings.Get(RoutingIntentClassifier.ProviderKey) == AnthropicIntentClassifier.Provider;
        IReplyWriter first = anthropicFirst ? anthropic : openAi, second = anthropicFirst ? openAi : anthropic;
        var outcome = await first.WriteAsync(request, settings, ct);
        if (outcome.Reply is not null || outcome.FailureReason == WriterOutcome.Timeout || ct.IsCancellationRequested)
        {
            return outcome;
        }

        var other = await second.WriteAsync(request, settings, ct);
        return other.Reply is not null ? other with { LatencyMs = outcome.LatencyMs + other.LatencyMs } : outcome;
    }
}
