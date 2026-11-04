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
/// v1.53 (B-34, layer 4): the reviewer of a free answer, through Anthropic (the writer's model), its answer held to the
/// schema {"ok": bool, "problem": …}. Fails closed: no key, an error, a late or invalid answer → «unavailable», and the
/// free answer does not go.
/// </summary>
public sealed class AnthropicReplyReviewer(IOptions<HazemOptions> options) : IReplyReviewer
{
    public const string PromptVersion = "review-v2";
    private const int MaxTokens = 1024; // v1.56: Haiku thinks before it answers; 256 cut the answer off (the sweep: «unavailable» 4 of 25)

    /// <summary>The whole review within this time (the approved words go rather than a late answer).</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    private static readonly Lazy<string> Prompt = new(() =>
    {
        using var s = typeof(AnthropicReplyReviewer).Assembly.GetManifestResourceStream($"Office.Infrastructure.Ai.prompts.{PromptVersion}.md")
            ?? throw new InvalidOperationException($"embedded prompt {PromptVersion}.md is missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    });

    private static readonly JsonObject Schema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("ok", "problem"),
        ["properties"] = new JsonObject
        {
            ["ok"] = new JsonObject { ["type"] = "boolean" },
            ["problem"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray("none", ReplyReview.Promise, ReplyReview.Legal, ReplyReview.NotInFacts, ReplyReview.Harmful, ReplyReview.BreaksPlaybook),
            },
        },
    };

    private AnthropicClient? _client;
    private string? _clientKey;

    public async Task<string?> ProblemAsync(ReviewRequest request, HazemSettings settings, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.AnthropicApiKey))
        {
            return ReplyReview.Unavailable + "_nokey";
        }

        var model = settings.Get(Conversational.ModelKey) is { Length: > 0 } m && m.StartsWith("claude", StringComparison.Ordinal) ? m : o.AnthropicModel;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);
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
                System = new List<TextBlockParam> { new() { Text = Prompt.Value, CacheControl = new CacheControlEphemeral() } },
                Messages = [new() { Role = Role.User, Content = Message(request) }],
                OutputConfig = new OutputConfig
                {
                    Effort = Effort.Low,
                    Format = new JsonOutputFormat { Schema = Schema.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value)) },
                },
            }, budget.Token);
            if (response.StopReason != "end_turn")
            {
                return ReplyReview.Unavailable + "_stop_" + (response.StopReason?.ToString() ?? "none");
            }

            return Verdict(response.Content.Select(b => b.Value).OfType<TextBlock>().LastOrDefault()?.Text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ReplyReview.Unavailable + "_timeout";
        }
        catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException or JsonException or InvalidOperationException)
        {
            return ReplyReview.Unavailable + "_error";
        }
    }

    /// <summary>Null for ok with no problem; the problem otherwise; «unavailable» for anything that is not the schema.</summary>
    public static string? Verdict(string? content)
    {
        try
        {
            if (content is null || JsonNode.Parse(content) is not JsonObject o || o["ok"] is not JsonValue ok || ok.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
            {
                return ReplyReview.Unavailable + "_invalid";
            }

            var problem = o["problem"] is JsonValue p && p.GetValueKind() == JsonValueKind.String ? p.GetValue<string>() : null;
            if (ok.GetValue<bool>() && problem is null or "none")
            {
                return null;
            }

            return problem is not null && ReplyReview.Problems.Contains(problem) ? problem : ReplyReview.Unavailable + "_invalid";
        }
        catch (JsonException)
        {
            return ReplyReview.Unavailable + "_invalid";
        }
    }

    private static string Message(ReviewRequest r)
    {
        var sb = new StringBuilder();
        sb.Append("REPLY TO REVIEW:\n").Append(r.Reply).Append('\n');
        sb.Append("OFFICE FACTS:\n").Append(r.Facts.Length > 0 ? r.Facts : "(none)").Append('\n');
        sb.Append("CONVERSATION (oldest first):\n");
        foreach (var line in r.History)
        {
            sb.Append(line.FromClient ? "العميل: " : "حازم: ").Append(line.Text.Replace('\n', ' ')).Append('\n');
        }

        return sb.ToString();
    }
}
