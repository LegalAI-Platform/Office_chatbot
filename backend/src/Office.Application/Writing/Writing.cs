using System.Text;
using System.Text.RegularExpressions;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Understanding;

namespace Office.Application.Writing;

// v1.41 (TASK 17): Hazem talks like a person. The code decides what is said (the approved text, already filled with the
// facts: the day, the time, his name as he typed it); a second AI call, «the writer», says it in its own words while it
// sees the conversation; the code checks every sentence before it goes, and anything doubtful sends the approved text.
// Only the everyday replies (Conversational.Purposes): never where to pay, a confirmation, a price list, a legal refusal,
// the Urfi questions or a template.

/// <summary>One line of the conversation, as the writer sees it (masked as for the classifier, T-09: text only).</summary>
public sealed record ChatLine(bool FromClient, string Text);

/// <summary>
/// What the writer is asked: the reply's purpose, the approved text it replaces (its facts are the only facts it may use),
/// how to call the client (« يا أستاذ محمد» or nothing), and the conversation, his message of this turn last.
/// </summary>
public sealed record WriteRequest(string Purpose, string Approved, string Call, IReadOnlyList<ChatLine> History);

/// <summary>One call to the writer: the sentence, or why there is none, and what it cost.</summary>
public sealed record WriterOutcome(
    string? Reply,
    string? FailureReason,
    string Model,
    string PromptVersion,
    int LatencyMs,
    int? InputTokens = null,
    int? OutputTokens = null)
{
    public const string NoKey = "no_key";
    public const string Timeout = "timeout";
    public const string Error = "error";
    public const string InvalidOutput = "invalid_output";
}

public interface IReplyWriter
{
    /// <summary>Never throws for an AI failure: the outcome says why.</summary>
    Task<WriterOutcome> WriteAsync(WriteRequest request, HazemSettings settings, CancellationToken ct);
}

/// <summary>The client's recent turns and Hazem's answers (hazem.turns), oldest first.</summary>
public interface IConversationHistory
{
    Task<IReadOnlyList<ChatLine>> RecentAsync(string waId, int turns, CancellationToken ct);
}

/// <summary>
/// What became of one written reply, kept in hazem.generated_replies: the text sent (null when the approved text went),
/// and <see cref="Guard"/> «passed» or why it was not used (a check of <see cref="ReplyGuard"/>, or the writer's failure).
/// </summary>
public sealed record WrittenReply(string IdempotencyKey, string Purpose, string? Text, string Guard, string Model, string PromptVersion,
    int LatencyMs, int? InputTokens, int? OutputTokens)
{
    public const string Passed = "passed";
}

/// <summary>
/// v1.41 (§5): the code's check of a written sentence. Null when it may go; else why not. The facts are the approved text's:
/// a number, an amount or a legal word the approved text does not have never goes out in the writer's words.
/// </summary>
public static partial class ReplyGuard
{
    public const int MaxChars = 300;

    /// <summary>Words of the law: the writer never explains it (Hazem never answers a legal question).</summary>
    private static readonly string[] LegalWords =
    [
        "مادة", "المادة", "قانون رقم", "القانون رقم", "لسنة", "حبس", "الحبس", "عقوبة", "العقوبة", "غرامة", "سجن", "جنحة", "جناية",
        "حكم", "الحكم", "المحكمة هت", "حقك القانوني", "حقوقك القانونية", "من حقك", "يحق لك", "ترفع قضية", "ترفع دعوى", "النيابة",
        "استئناف", "نقض", "طعن",
    ];

    /// <summary>Promises and certainty the office never gives.</summary>
    private static readonly string[] Promises =
    [
        "مضمون", "ضمان", "أكيد هتكسب", "اكيد هتكسب", "هتكسب", "هنكسب", "100%", "١٠٠٪", "خلال ساعة", "خلال ساعه", "حالًا", "حالا",
        "فورًا", "فورا", "دلوقتي حالا", "مجانًا", "مجانا", "ببلاش", "خصم", "عرض خاص",
    ];

    private static readonly string[] Money = ["جنيه", "جنية", "ج.م", "دولار", "ريال", "فلوس", "تحويل", "انستا", "InstaPay", "محفظة", "كاش"];

    public static string? Problem(string? reply, WriteRequest request, string? previous)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return "empty";
        }

        var text = reply.Trim();
        if (text.Length > MaxChars)
        {
            return "too_long";
        }

        if (Link().IsMatch(text))
        {
            return "link";
        }

        var letters = text.Count(char.IsLetter);
        if (letters == 0 || text.Count(c => c is >= '؀' and <= 'ۿ') * 2 < letters)
        {
            return "not_arabic";
        }

        // Every number in the sentence is one of the approved text's (Arabic-Indic digits read as digits).
        var allowed = Numbers(request.Approved).ToHashSet(StringComparer.Ordinal);
        if (Numbers(text).Any(n => !allowed.Contains(n)))
        {
            return "number_not_given";
        }

        string? Word(string[] words) => words.FirstOrDefault(w =>
            text.Contains(w, StringComparison.OrdinalIgnoreCase) && !request.Approved.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (Word(LegalWords) is not null)
        {
            return "legal_word";
        }

        if (Word(Promises) is not null)
        {
            return "promise";
        }

        if (Word(Money) is not null)
        {
            return "money";
        }

        if (previous is not null && Normal(previous) == Normal(text))
        {
            return "same_as_previous";
        }

        return null;
    }

    /// <summary>The digit runs of a text, Arabic-Indic and Persian digits read as ASCII.</summary>
    public static IEnumerable<string> Numbers(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                _ => c,
            });
        }

        return Digits().Matches(sb.ToString()).Select(m => m.Value);
    }

    private static string Normal(string s) => Space().Replace(s, " ").Trim();

    [GeneratedRegex(@"[0-9]+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"https?://|www\.|@|\.com\b", RegexOptions.IgnoreCase)]
    private static partial Regex Link();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}

/// <summary>
/// v1.41 (TASK 17): which approved texts the writer may say in its own words, and what each one is for (told to the writer).
/// Everything else (where to pay, confirmations, the services list, prices, the legal refusal, the Urfi questions, the name
/// and number questions, the abuse warning, the rating, templates) always goes as approved.
/// </summary>
public static class Conversational
{
    /// <summary>hazem.settings: the written replies on (true) or off (anything else: exactly the approved texts).</summary>
    public const string SwitchKey = "hazem.conversational";

    /// <summary>hazem.settings: the writer's model (empty: the classifier's model).</summary>
    public const string ModelKey = "ai.writer_model";

    /// <summary>The client's recent turns the writer sees.</summary>
    public const int HistoryTurns = 10;

    public static IReadOnlyDictionary<string, string> Purposes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TextKeys.Greeting] = "answer his greeting and ask how you can help",
        [TextKeys.Greeting2] = "answer his greeting and ask how you can help",
        [TextKeys.Greeting3] = "answer his greeting and ask how you can help",
        [TextKeys.Welcome] = "introduce yourself as the office's digital assistant and ask whether he wants to see the office's services or tell his matter (two buttons follow your sentence)",
        [TextKeys.WelcomeChoose] = "ask whether he wants to see the office's services or tell his matter (two buttons follow your sentence)",
        [TextKeys.WelcomeBack] = "welcome him back by the first name given",
        [TextKeys.SituationUrgent] = "remind him he has a paid urgent consultation and when the lawyer will call; he may write his matter here and it reaches the lawyer",
        [TextKeys.SituationHeld] = "remind him which appointment is held for him and until when, waiting only for the transfer's photo",
        [TextKeys.SituationConfirmed] = "remind him his appointment is confirmed, its day and time; he may write his matter here before it",
        [TextKeys.SituationOrder] = "tell him where his service request stands; he may ask about it here",
        [TextKeys.SituationTopic] = "tell him his matter arrived and the lawyer will see it",
        [TextKeys.TopicReceived] = "tell him his matter arrived",
        [TextKeys.TopicReceived2] = "tell him his matter arrived",
        [TextKeys.TopicReceived3] = "tell him his matter arrived",
        [TextKeys.ServicesTell] = "ask him to tell his matter briefly",
        [TextKeys.Unclear] = "say kindly you did not understand and ask what he needs",
        [TextKeys.RepeatChoice] = "he does not seem to get what he needs: ask kindly whether he wants the office's services or someone from the office (two buttons follow your sentence)",
        [TextKeys.AboutHazem] = "say who you are: the digital assistant of the office, not a person",
        [TextKeys.OffTopic] = "say politely that this is outside what a law office does, and that you are here for the office's services",
        [TextKeys.Thanks] = "answer his thanks warmly and briefly",
    };

    /// <summary>The greeting answered in kind (B-01.6, «وعليكم السلام»…) joins the written sentence it opens.</summary>
    private const string GreetPrefix = "greet_";

    public static bool MayWrite(string key) => Purposes.ContainsKey(key) || key.StartsWith(GreetPrefix, StringComparison.Ordinal);

    /// <summary>The turn's answer with its first run of everyday texts said by the writer, and what became of it.</summary>
    public sealed record Rewritten(ReplyComposer.Composed Composed, WrittenReply? Written);

    /// <summary>
    /// The first run of consecutive actions whose texts may be written (a buttons message only at its end) becomes one
    /// message in the writer's words: the first action's key, the last one's buttons. The check fails, the writer is down or
    /// slow → the turn goes exactly as composed. Nothing to write → unchanged, no call.
    /// </summary>
    public static async Task<Rewritten> RewriteAsync(ReplyComposer.Composed composed, IReplyWriter writer, IConversationHistory history,
        Turn turn, string call, HazemSettings settings, CancellationToken ct)
    {
        var keyOf = composed.Records.GroupBy(r => r.IdempotencyKey).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
        int start = -1, end = -1;
        for (var i = 0; i < composed.Actions.Count; i++)
        {
            var a = composed.Actions[i];
            var ok = keyOf.TryGetValue(a.IdempotencyKey, out var k) && MayWrite(k)
                     && (a.Type == ActionType.SendText || a.Type == ActionType.SendInteractive);
            if (!ok)
            {
                if (start >= 0)
                {
                    break;
                }

                continue;
            }

            start = start < 0 ? i : start;
            end = i;
            if (a.Type == ActionType.SendInteractive)
            {
                break; // buttons close the run
            }
        }

        if (start < 0)
        {
            return new Rewritten(composed, null);
        }

        var run = composed.Actions.Skip(start).Take(end - start + 1).ToList();
        // The run's main text: its last one that has a purpose (a greeting in kind only opens it).
        var mainKey = run.Select(a => keyOf[a.IdempotencyKey]).LastOrDefault(Purposes.ContainsKey);
        if (mainKey is null)
        {
            return new Rewritten(composed, null); // a greeting in kind alone stays as approved
        }

        var approved = string.Join("\n", run.Select(a => a.Text));
        var lines = (await history.RecentAsync(turn.WaId, HistoryTurns, ct)).ToList();
        if (AiInput.Build(turn) is { } now)
        {
            lines.Add(new ChatLine(true, now));
        }

        var previous = lines.LastOrDefault(l => !l.FromClient)?.Text;
        var request = new WriteRequest(Purposes[mainKey], approved, call.Trim(), lines);
        var outcome = await writer.WriteAsync(request, settings, ct);
        var problem = outcome.Reply is null ? outcome.FailureReason ?? WriterOutcome.Error : ReplyGuard.Problem(outcome.Reply, request, previous);
        var first = run[0];
        var written = new WrittenReply(first.IdempotencyKey, mainKey, problem is null ? outcome.Reply!.Trim() : null, problem ?? WrittenReply.Passed,
            outcome.Model, outcome.PromptVersion, outcome.LatencyMs, outcome.InputTokens, outcome.OutputTokens);
        if (problem is not null)
        {
            return new Rewritten(composed, written);
        }

        var last = run[^1];
        var merged = first with
        {
            Type = last.Type,
            Text = written.Text,
            Interactive = last.Interactive,
        };
        var dropped = run.Skip(1).Select(a => a.IdempotencyKey).ToHashSet(StringComparer.Ordinal);
        var actions = composed.Actions.Where(a => !dropped.Contains(a.IdempotencyKey)).Select(a => a.IdempotencyKey == first.IdempotencyKey ? merged : a).ToList();
        // One record for the merged message: the main text's key and version under the first action's key.
        var main = composed.Records.First(r => r.Key == mainKey && run.Any(a => a.IdempotencyKey == r.IdempotencyKey));
        var records = composed.Records.Where(r => !run.Any(a => a.IdempotencyKey == r.IdempotencyKey)).ToList();
        records.Insert(composed.Records.TakeWhile(r => r.IdempotencyKey != first.IdempotencyKey).Count(), main with { IdempotencyKey = first.IdempotencyKey, Part = 0 });
        return new Rewritten(new ReplyComposer.Composed(actions, records, composed.Skipped), written);
    }
}
