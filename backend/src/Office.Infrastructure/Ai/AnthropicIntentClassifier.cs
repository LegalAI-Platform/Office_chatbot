using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using Office.Application.Responders;
using Office.Application.Understanding;

namespace Office.Infrastructure.Ai;

/// <summary>
/// v1.26: the same prompt (<see cref="OpenAiIntentClassifier.PromptVersion"/>) and the same closed lists through Anthropic's
/// Messages API, its output held to the schema (structured outputs). The prompt is cached (it is the same every turn); no
/// sampling parameter is sent (the API refuses them since 2026-10-21), the effort is low (a classification). Never throws for
/// an AI failure: the outcome says why. The key (HAZEM_ANTHROPIC_API_KEY) is never logged.
/// </summary>
public sealed class AnthropicIntentClassifier(IOptions<HazemOptions> options, TimeProvider clock) : IIntentClassifier
{
    public const string Provider = "anthropic";

    /// <summary>Enough for the JSON and the low-effort thinking before it.</summary>
    private const int MaxTokens = 2048;

    /// <summary>Total time for the call and its one retry, as for OpenAI.</summary>
    public TimeSpan Budget { get; init; } = OpenAiIntentClassifier.DefaultBudget;

    private static readonly Dictionary<string, JsonElement> Schema =
        OpenAiIntentClassifier.Schema.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));

    private AnthropicClient? _client;
    private string? _clientKey;

    public async Task<ClassifierOutcome> ClassifyAsync(string text, HazemSettings settings, CancellationToken ct)
    {
        var o = options.Value;
        var started = clock.GetTimestamp();
        ClassifierOutcome Done(RawClassification? answer, string? failure, int? input = null, int? output = null) =>
            new(answer, failure, o.AnthropicModel, OpenAiIntentClassifier.PromptVersion, (int)clock.GetElapsedTime(started).TotalMilliseconds,
                input, output);

        if (string.IsNullOrWhiteSpace(o.AnthropicApiKey))
        {
            return Done(null, ClassifierOutcome.NoKey);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);
        try
        {
            var response = await Client(o.AnthropicApiKey).Messages.Create(new MessageCreateParams
            {
                Model = o.AnthropicModel,
                MaxTokens = MaxTokens,
                System = new List<TextBlockParam> { new() { Text = OpenAiIntentClassifier.PromptText, CacheControl = new CacheControlEphemeral() } },
                Messages = [new() { Role = Role.User, Content = text }],
                OutputConfig = new OutputConfig { Effort = Effort.Low, Format = new JsonOutputFormat { Schema = Schema } },
            }, budget.Token);

            var usage = response.Usage;
            var input = (int)(usage.InputTokens + (usage.CacheReadInputTokens ?? 0) + (usage.CacheCreationInputTokens ?? 0));
            if (response.StopReason != "end_turn")
            {
                return Done(null, ClassifierOutcome.InvalidOutput, input, (int)usage.OutputTokens); // a refusal, or cut by max_tokens
            }

            var json = response.Content.Select(b => b.Value).OfType<TextBlock>().LastOrDefault()?.Text;
            var (answer, failure) = OpenAiIntentClassifier.Parse(json);
            return Done(answer, failure, input, (int)usage.OutputTokens);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Done(null, ClassifierOutcome.Timeout);
        }
        catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException or JsonException or InvalidOperationException)
        {
            return Done(null, ClassifierOutcome.Error);
        }
    }

    /// <summary>One client per key (its connections are reused); one retry inside the budget.</summary>
    private AnthropicClient Client(string key)
    {
        if (_client is null || _clientKey != key)
        {
            _client = new AnthropicClient { ApiKey = key, MaxRetries = 1 };
            _clientKey = key;
        }

        return _client;
    }
}

/// <summary>
/// v1.26: hazem.settings ai.provider picks who classifies (openai, the default, or anthropic). When it gives no answer
/// (no key, no credits, an error) the other one is asked in the same turn; a timeout is not asked again (the turn would
/// wait twice). The outcome's model says who answered.
/// </summary>
public sealed class RoutingIntentClassifier(IIntentClassifier openAi, IIntentClassifier anthropic) : IIntentClassifier
{
    public const string ProviderKey = "ai.provider";

    public async Task<ClassifierOutcome> ClassifyAsync(string text, HazemSettings settings, CancellationToken ct)
    {
        var anthropicFirst = settings.Get(ProviderKey) == AnthropicIntentClassifier.Provider;
        var (first, second) = anthropicFirst ? (anthropic, openAi) : (openAi, anthropic);
        var outcome = await first.ClassifyAsync(text, settings, ct);
        if (outcome.Answer is not null || outcome.FailureReason == ClassifierOutcome.Timeout || ct.IsCancellationRequested)
        {
            return outcome;
        }

        var other = await second.ClassifyAsync(text, settings, ct);
        return other.Answer is not null ? other with { LatencyMs = outcome.LatencyMs + other.LatencyMs } : outcome;
    }
}
