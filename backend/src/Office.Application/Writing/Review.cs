using Office.Application.Responders;

namespace Office.Application.Writing;

// v1.53 (B-34, layer 4, the owner: «اي احسن طريقه هتغطي كل الكيسز»): a free answer is read by a second AI call before it
// goes, for its MEANING, not for words: a promise the office cannot keep («مراتك مش هتعرف أبدًا»), legal information, a
// fact the office facts do not hold, or anything that harms the office. The word lists of ReplyGuard stay first; this one
// catches what no list can. Unsure, down or slow → the free answer never goes (its approved words do).

/// <summary>What the reviewer reads: the reply, the facts it may use, and the conversation it answers.</summary>
public sealed record ReviewRequest(string Reply, string Facts, IReadOnlyList<ChatLine> History)
{
    /// <summary>v1.58: where the reviewer notes what its call used (the caller adds it to the turn's usage).</summary>
    public List<Office.Application.Costs.AiUsage>? Usage { get; init; }
}

public interface IReplyReviewer
{
    /// <summary>Null when the reply may go; else why not (<see cref="ReplyReview"/>). Never throws for an AI failure.</summary>
    Task<string?> ProblemAsync(ReviewRequest request, HazemSettings settings, CancellationToken ct);
}

/// <summary>The reviewer's answers, kept as the written reply's guard («review_promise»…).</summary>
public static class ReplyReview
{
    public const string Prefix = "review_";
    public const string Promise = "promise";
    public const string Legal = "legal";
    public const string NotInFacts = "not_in_facts";
    public const string Harmful = "harmful";

    /// <summary>v1.57: the reply breaks a rule of «دليل التعامل» (the owner's rules), whatever the topic.</summary>
    public const string BreaksPlaybook = "breaks_playbook";

    /// <summary>The reviewer could not answer: the reply does not go. v1.56: always with its reason («unavailable_timeout»…).</summary>
    public const string Unavailable = "unavailable";

    public static readonly string[] Problems = [Promise, Legal, NotInFacts, Harmful, BreaksPlaybook];
}

/// <summary>
/// v1.56: a reply the checks refused is written ONCE more with the reason fed back to the writer («it promises something»,
/// «it repeats what was sent»…), before the approved words go. Only what the writer can change: never a failed call or a
/// reviewer that could not answer. The second reply goes through every check again; refused again → the approved words.
/// </summary>
public static class ReplyRetry
{
    public static bool Allows(string problem) =>
        !problem.StartsWith(ReplyReview.Prefix + ReplyReview.Unavailable, StringComparison.Ordinal);

    /// <summary>What the writer is told about its refused reply (in English, as its prompt is).</summary>
    public static string Feedback(string problem, string reply, string? detail = null) => $"""
        {reply.Trim()}
        WHY IT WAS REFUSED: {Why(problem)}{(detail is null ? "" : $" These numbers must appear exactly as written in the approved text: {detail}.")}
        """;

    private static string Why(string problem) => problem switch
    {
        "review_promise" or "promise" =>
            "It promises or guarantees something the office cannot control (for example that nobody will know, a result, or a time). Say only what is true: the facts, that what he tells the office stays private with the office, and the next step.",
        "review_legal" or "legal_word" =>
            "It contains legal information or a legal word. Never explain the law: say kindly that the lawyer answers it in the consultation.",
        "review_not_in_facts" or "number_not_given" =>
            "It states a fact, a number or a rule that is not in the APPROVED TEXT or the OFFICE FACTS. Use only those.",
        "review_breaks_playbook" =>
            "It breaks a rule of «دليل التعامل» (the owner's rules, in the OFFICE FACTS). Read the rules again and write it following every one of them.",
        "review_harmful" =>
            "It could harm the office or the client (another lawyer, pressure, judging him). Write it kindly without that.",
        "money" => "It speaks of money or paying beyond the approved text. Leave that out.",
        "self_feminine" => "You wrote «فاهمة»: you, Hazem, are a man («فاهم»).",
        "typo" => "It has a spelling mistake: check every word.",
        "repeats_before" =>
            "It repeats what was already sent just before it in this turn, or points to it. Do not repeat it: go on with what is new.",
        "same_as_previous" => "It is the same as your previous message. Say it differently and shorter.",
        "too_long" => "It is too long: at most three short sentences.",
        "link" or "not_arabic" => "No links, e-mails or foreign words: Arabic only.",
        "tail_repeated" => "Do not write the address, the hours or the map: they are added right after your sentence. Write only the answer to his question.",
        "price_dropped" => "It dropped the price the client asked about: say it exactly as in the approved text.",
        "refusal_dropped" => "It must say that the lawyer himself answers this in the consultation.",
        "asked_back" => "Do not end with a question: the handoff is done.",
        "facts_and_office" => "Do not also say that the office will answer: you answered from the facts.",
        "said_confirmed" => "The transfer is still under the office's review: never say it is confirmed.",
        _ => "It was refused by a check: write it plainly, using only the APPROVED TEXT and the OFFICE FACTS.",
    };
}
