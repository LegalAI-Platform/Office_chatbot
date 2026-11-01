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
public sealed record WriteRequest(string Purpose, string Approved, string Call, IReadOnlyList<ChatLine> History)
{
    /// <summary>v1.45 (TASK 18): «حقايق المكتب», filled; the writer answers his question from them, and the guard allows their facts.</summary>
    public string Facts { get; init; } = "";

    /// <summary>v1.45: «woman», «man» or «unknown», for the verb forms («تحبي» / «تحب»).</summary>
    public string Client { get; init; } = ClientGender.Unknown;

    /// <summary>v1.45: the approved messages sent just before this one in the same turn (the price, the address): never repeated.</summary>
    public string Before { get; init; } = "";
}

/// <summary>
/// v1.45 (TASK 18 §3.4): who the writer talks to, for the verb forms. The gender of the name he typed, and his own words when
/// they say otherwise: a woman writes «عايزه»، «محتاجه»، «جوزي».
/// </summary>
public static class ClientGender
{
    public const string Woman = "woman";
    public const string Man = "man";
    public const string Unknown = "unknown";

    private static readonly string[] WomanWords =
        ["عايزه", "عاوزه", "محتاجه", "خايفه", "مش عارفه", "جوزي", "طليقي", "انا ست", "أنا ست", "انا حامل", "أنا حامل", "متجوزة", "مطلقة"];

    /// <summary>
    /// v1.45: the approved texts' words to a man, and the same words to a woman. Only verbs and adjectives that speak to him;
    /// never a number, a name, an amount or a payment detail (the text stays the approved one otherwise).
    /// </summary>
    private static readonly (string Man, string Woman)[] ToWoman =
    [
        ("حوّلت", "حوّلتي"), ("حوّل", "حوّلي"), ("حول", "حولي"), ("ابعت", "ابعتي"), ("اختار", "اختاري"), ("اكتب", "اكتبي"),
        ("تحب", "تحبي"), ("حابب", "حابة"), ("عايز", "عايزة"), ("محتاج", "محتاجة"), ("دوس", "دوسي"), ("اضغط", "اضغطي"),
        ("قولّي", "قوليلي"), ("تقدر", "تقدري"), ("هتدفع", "هتدفعي"),
    ];

    /// <summary>The text with its words to him said to her (a whole word only: «تحب» never inside «تحبي»).</summary>
    public static string Feminine(string text)
    {
        foreach (var (man, woman) in ToWoman)
        {
            text = Regex.Replace(text, $@"(?<![\p{{L}}\p{{Mn}}]){Regex.Escape(man)}(?![\p{{L}}\p{{Mn}}])", woman);
        }

        return text;
    }

    public static string Of(string? nameGender, IEnumerable<ChatLine> lines)
    {
        var words = string.Join(" ", lines.Where(l => l.FromClient).Select(l => l.Text));
        if (WomanWords.Any(w => words.Contains(w, StringComparison.Ordinal)))
        {
            return Woman;
        }

        return nameGender switch
        {
            "female" => Woman,
            "male" => Man,
            _ => Unknown,
        };
    }
}

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
        "استئناف", "نقض", "طعن", "حقك", "حقوقك", // v1.45: «مستحقاتك حقك» is an opinion on his rights
    ];

    /// <summary>Promises and certainty the office never gives.</summary>
    private static readonly string[] Promises =
    [
        "مضمون", "ضمان", "أكيد هتكسب", "اكيد هتكسب", "هتكسب", "هنكسب", "100%", "١٠٠٪", "خلال ساعة", "خلال ساعه", "حالًا", "حالا",
        "فورًا", "فورا", "دلوقتي حالا", "مجانًا", "مجانا", "ببلاش", "خصم", "عرض خاص", "بالظبط", "بالضبط",
    ];

    private static readonly string[] Money = ["جنيه", "جنية", "ج.م", "دولار", "ريال", "فلوس", "تحويل", "انستا", "InstaPay", "محفظة", "كاش",
        "حوّل", "حول ", "حولي", "ادفع", "إدفع", "تدفع", "هتدفع"]; // v1.45: the verbs of paying too

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

        // Every number in the sentence is one of the approved text's or the office facts' (Arabic-Indic digits read as digits).
        var given = request.Approved + "\n" + request.Facts;
        var allowed = Numbers(given).ToHashSet(StringComparer.Ordinal);
        if (Numbers(text).Any(n => !allowed.Contains(n)))
        {
            return "number_not_given";
        }

        string? Word(string[] words) => words.FirstOrDefault(w =>
            text.Contains(w, StringComparison.OrdinalIgnoreCase) && !given.Contains(w, StringComparison.OrdinalIgnoreCase));
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

        // v1.45: Hazem speaks of himself as a man (the writer once wrote «فاهمة» to a woman)
        if (text.Contains("فاهمة", StringComparison.Ordinal))
        {
            return "self_feminine";
        }

        // v1.45: a word the writer misspelt at its end («إيهؤ»), or «حضرتي» for «حضرتك»
        if (Typo().IsMatch(text) || text.Contains("حضرتي", StringComparison.Ordinal))
        {
            return "typo";
        }

        // v1.45: what the office sent just before it in the same turn (a price, the address, the legal refusal) is never said
        // again, and never pointed to («اتقال فوق»): four words in a row from it, or a pointer, send the approved text instead.
        if (request.Before.Length > 0 && (Pointer().IsMatch(text) || SharesFourWords(text, request.Before)))
        {
            return "repeats_before";
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

        // v1.45: «1,000» and «1000» are one number (the thousands' comma, Arabic «٬» too, between digits)
        return Digits().Matches(Thousands().Replace(sb.ToString(), "")).Select(m => m.Value);
    }

    [GeneratedRegex(@"(?<=[0-9])[,٬](?=[0-9]{3})")]
    private static partial Regex Thousands();

    private static string Normal(string s) => Space().Replace(s, " ").Trim();

    private static bool SharesFourWords(string text, string before)
    {
        static string[] Words(string s) => Regex.Split(s, @"[^\p{L}\p{N}]+").Where(w => w.Length > 0).ToArray();
        var b = Words(before);
        var said = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 4 <= b.Length; i++)
        {
            said.Add(string.Join(' ', b, i, 4));
        }

        var t = Words(text);
        for (var i = 0; i + 4 <= t.Length; i++)
        {
            if (said.Contains(string.Join(' ', t, i, 4)))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\p{L}ؤ(?=$|[\s؟?.!،,])")]
    private static partial Regex Typo();

    [GeneratedRegex(@"فوق|اتقال|اتبعتلك|زي ما قلت|زي ما قولت|زي ما ذكرت")]
    private static partial Regex Pointer();

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
        [TextKeys.SituationUrgent] = "remind him he has a paid urgent consultation and when the lawyer will call",
        [TextKeys.SituationHeld] = "remind him which appointment is held for him and until when, waiting only for the transfer's photo",
        [TextKeys.SituationConfirmed] = "remind him his appointment is confirmed, its day and time",
        [TextKeys.SituationReview] = "remind him his appointment is held for him, its day and time; the office is reviewing his transfer and will send him the confirmation (never say it is confirmed)",
        [TextKeys.SituationAskTopic] = "invite him, if he likes, to write his matter briefly here so it reaches the lawyer before the appointment",
        [TextKeys.SituationOrder] = "tell him where his service request stands; he may ask about it here",
        [TextKeys.SituationTopic] = "tell him his matter arrived and the lawyer will see it",
        [TextKeys.TopicReceived] = "tell him his matter arrived",
        [TextKeys.TopicReceived2] = "tell him his matter arrived",
        [TextKeys.TopicReceived3] = "tell him his matter arrived",
        // v1.46: «انا تعبان» gets a word of comfort before the question, never a cold «not clear»
        [TextKeys.ServicesTell] = "ask him to tell his matter briefly (a short word of comfort first when he says he is tired, upset or in trouble)",
        [TextKeys.Unclear] = "say kindly you did not understand and ask what he needs (a short word of comfort first when he sounds tired or upset)",
        [TextKeys.RepeatChoice] = "he does not seem to get what he needs: ask kindly whether he wants the office's services or someone from the office (two buttons follow your sentence)",
        [TextKeys.AboutHazem] = "say who you are: the digital assistant of the office, not a person",
        [TextKeys.OffTopic] = "say politely that this is outside what a law office does, and that you are here for the office's services",
        [TextKeys.Thanks] = "answer his thanks warmly and briefly",
        // v1.45 (TASK 18 §3.3): the answers to his questions and the offer, so a question gets its answer, in one message
        [TextKeys.AboutLawyer] = "answer what he asked about the lawyer, from the text and the office facts (who he is; every consultation is with him himself)",
        [TextKeys.ConsultationOffer] = "offer a consultation with the lawyer, by phone or video or at the office, with the prices given",
        [TextKeys.CaseOffer] = "tell him the lawyer first hears his matter in a consultation and then sets the fees, and offer the consultation with the prices given",
        [TextKeys.OfficeHowToBook] = "tell him how booking goes",
        [TextKeys.OfficeRefund] = "tell him the refund rule",
        [TextKeys.OfficeFeePolicy] = "tell him how the fees of a case are set",
        [TextKeys.ServiceCheck] = "tell him the office will answer him here on whether it does this",
        [TextKeys.ServiceDetail] = "tell him his question reached the office and they will answer it here (never the answer yourself)",
        [TextKeys.OfficePrivacy] = "reassure him that everything he tells the office stays private: the office never discloses its clients' secrets",
        [TextKeys.AskCallApp] = "ask where the lawyer should call him: WhatsApp or FaceTime (two buttons follow your sentence)",
        [TextKeys.BookingStepAgain] = "ask him kindly to pick from the choices below, or to write the day and time that suit him (the choices follow your sentence)",
        [TextKeys.BookingChooseSlot] = "ask him to pick the time that suits him from the list (the list follows your sentence)",
        [TextKeys.AskNameBooking] = "ask his name to register the booking (a word of comfort first if he is worried)",
        [TextKeys.HumanRequest] = "tell him you are letting the office know he wants to talk to someone, and they will answer him here",
        // v1.45 (the owner: «اه»): the price he asked and the legal refusal join the next step in one message; their prices are
        // kept to the letter and the refusal always says the lawyer answers it in the consultation (Locked, MustSay)
        [TextKeys.LegalQuestion] = "say kindly that the lawyer himself answers this in the consultation (never any legal information)",
        [TextKeys.UrgentPrice] = "tell him the urgent consultation's price and length",
        [TextKeys.PriceDocumentReview] = "tell him the price of reviewing a contract or papers, and that the lawyer sets the final price before payment",
        [TextKeys.PriceContractDrafting] = "tell him the starting price of drafting a contract, and that the lawyer sets the final price",
        [TextKeys.PriceFormalNotice] = "tell him the price of a formal notice",
        [TextKeys.PriceUrf] = "tell him the Urfi contract's prices",
        [TextKeys.WrittenOpinion] = "tell him a written opinion comes after the consultation and its price",
        [TextKeys.PriceCaseRepresentation] = "tell him the lawyer sets a case's fees after hearing the matter (never a number for them)",
    };

    /// <summary>v1.45: texts that only follow another one in the same message (never the message's main purpose).</summary>
    private static readonly HashSet<string> AddOns = [TextKeys.SituationAskTopic];

    /// <summary>v1.45: texts whose numbers (the price he asked) must all be in the written message.</summary>
    private static readonly HashSet<string> Locked =
    [
        TextKeys.UrgentPrice, TextKeys.PriceDocumentReview, TextKeys.PriceContractDrafting, TextKeys.PriceFormalNotice, TextKeys.PriceUrf,
        TextKeys.WrittenOpinion,
    ];

    /// <summary>v1.45 (TASK 18 §3.2): the texts of «حقايق المكتب», in the order the writer reads them.</summary>
    public static readonly string[] FactKeys = [TextKeys.OfficeFacts, TextKeys.OfficeFactsMore, TextKeys.OfficeFactsOther];

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
        Turn turn, string call, HazemSettings settings, CancellationToken ct, string facts = "", string? nameGender = null)
    {
        var lines = (await history.RecentAsync(turn.WaId, HistoryTurns, ct)).ToList();
        if (AiInput.Build(turn) is { } now)
        {
            lines.Add(new ChatLine(true, now));
        }

        var client = ClientGender.Of(nameGender, lines);
        var r = await WriteRunAsync(composed, writer, turn, call, settings, ct, facts, lines, client);
        if (client != ClientGender.Woman)
        {
            return r;
        }

        // v1.45 (TASK 18 §3.4): to a woman, the approved texts in her forms («حوّلي»، «اختاري»); the written one already is
        var writtenKey = r.Written?.Text is not null ? r.Written.IdempotencyKey : null;
        var actions = r.Composed.Actions
            .Select(a => a.Text is { } t && a.IdempotencyKey != writtenKey ? a with { Text = ClientGender.Feminine(t) } : a).ToList();
        return r with { Composed = new ReplyComposer.Composed(actions, r.Composed.Records, r.Composed.Skipped) };
    }

    private static async Task<Rewritten> WriteRunAsync(ReplyComposer.Composed composed, IReplyWriter writer, Turn turn, string call,
        HazemSettings settings, CancellationToken ct, string facts, List<ChatLine> lines, string client)
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
        // v1.45: an add-on («لو حابب تكتب الموضوع…») never takes the main text's place; the writer is told every purpose.
        var mainKey = run.Select(a => keyOf[a.IdempotencyKey]).LastOrDefault(k => Purposes.ContainsKey(k) && !AddOns.Contains(k))
                      ?? run.Select(a => keyOf[a.IdempotencyKey]).LastOrDefault(Purposes.ContainsKey);
        if (mainKey is null)
        {
            return new Rewritten(composed, null); // a greeting in kind alone stays as approved
        }

        var approved = string.Join("\n", run.Select(a => a.Text));
        var previous = lines.LastOrDefault(l => !l.FromClient)?.Text;
        var purpose = string.Join("; then ", run.Select(a => keyOf[a.IdempotencyKey]).Where(Purposes.ContainsKey).Select(k => Purposes[k]).Distinct());
        var request = new WriteRequest(purpose, approved, call.Trim(), lines)
        {
            Facts = facts, Client = client,
            Before = string.Join("\n", composed.Actions.Take(start).Select(a => a.Text).Where(t => t is { Length: > 0 })),
        };
        var outcome = await writer.WriteAsync(request, settings, ct);
        var problem = outcome.Reply is null ? outcome.FailureReason ?? WriterOutcome.Error : ReplyGuard.Problem(outcome.Reply, request, previous);
        // v1.45 (the owner): a price the client asked goes out to the letter, and the legal refusal always says the lawyer
        // answers it in the consultation; otherwise the approved texts go as they are
        var keys = run.Select(a => keyOf[a.IdempotencyKey]).ToList();
        var kept = run.Where(a => Locked.Contains(keyOf[a.IdempotencyKey])).SelectMany(a => ReplyGuard.Numbers(a.Text ?? "")).ToHashSet(StringComparer.Ordinal);
        if (problem is null && kept.Count > 0 && !kept.IsSubsetOf(ReplyGuard.Numbers(outcome.Reply!)))
        {
            problem = "price_dropped";
        }

        if (problem is null && keys.Contains(TextKeys.LegalQuestion)
            && !(outcome.Reply!.Contains("المستشار", StringComparison.Ordinal) && outcome.Reply.Contains("استشار", StringComparison.Ordinal)))
        {
            problem = "refusal_dropped";
        }

        if (problem is null && mainKey == TextKeys.SituationReview
            && (outcome.Reply!.Contains("متأكد", StringComparison.Ordinal) || outcome.Reply.Contains("اتأكد", StringComparison.Ordinal)))
        {
            problem = "said_confirmed"; // v1.45: the transfer is still under the office's review
        }
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
