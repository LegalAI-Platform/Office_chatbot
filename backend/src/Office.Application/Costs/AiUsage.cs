namespace Office.Application.Costs;

// v1.58 (the owner, 2026-11-04): «المحادثة ما بين الشات بوت والعميل صرفت قد إيه من الكريدت؟ خد كام توكن، عايز أحدد كل
// رسالة». Every AI call of a turn (the classifier, the writer, its retry, the reviewer) leaves one row in hazem.ai_usage with
// its tokens and its cost, so the panel can say what a conversation and each of its messages cost.

/// <summary>
/// What one call used: <see cref="Uncached"/> input tokens at the full price, <see cref="CacheRead"/> read from the prompt cache
/// (a twentieth of it), <see cref="CacheWrite"/> written to it (a quarter more), and the <see cref="Output"/>.
/// </summary>
public sealed record TokenUsage(int Uncached, int CacheRead, int CacheWrite, int Output)
{
    public int Input => Uncached + CacheRead + CacheWrite;
}

/// <summary>One AI call of a turn.</summary>
public sealed record AiUsage(string Kind, string Model, TokenUsage Tokens, int? LatencyMs = null);

public static class AiUsageKind
{
    /// <summary>The classifier: what the client meant.</summary>
    public const string Classify = "classify";

    /// <summary>The writer: the reply in its words (a retry is a second row).</summary>
    public const string Write = "write";

    /// <summary>The reviewer: the reply read for its meaning before it goes.</summary>
    public const string Review = "review";
}

/// <summary>
/// The price of a model per million tokens (USD): input, output, cache read, cache write (five minutes, a quarter more than the
/// input). Anthropic's published prices; a model not listed has no cost (its tokens are still counted, the panel says «بدون سعر»).
/// Voice-note transcripts (OpenAI Whisper, in n8n) are not counted here.
/// </summary>
public static class AiPrices
{
    private sealed record Price(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite);

    // longest id first: «claude-opus-5-5» before «claude-opus-5»
    private static readonly (string Model, Price Price)[] Prices =
    [
        ("claude-fable-5-1", new(10m, 50m, 0.25m, 12.5m)),
        ("claude-fable-5", new(10m, 50m, 0.25m, 12.5m)),
        ("claude-opus-5-5", new(4m, 20m, 0.20m, 5m)),
        ("claude-opus-5", new(5m, 25m, 0.25m, 6.25m)),
        ("claude-sonnet-5-5", new(2m, 10m, 0.20m, 2.5m)),
        ("claude-sonnet-5", new(2m, 10m, 0.20m, 2.5m)),
        ("claude-haiku-5-5", new(0.10m, 0.50m, 0.01m, 0.125m)),
    ];

    /// <summary>The cost in USD, or null when the model has no known price.</summary>
    public static decimal? Cost(string model, TokenUsage t)
    {
        foreach (var (id, p) in Prices.OrderByDescending(x => x.Model.Length))
        {
            if (model.StartsWith(id, StringComparison.Ordinal))
            {
                return (t.Uncached * p.Input + t.CacheRead * p.CacheRead + t.CacheWrite * p.CacheWrite + t.Output * p.Output) / 1_000_000m;
            }
        }

        return null;
    }
}
