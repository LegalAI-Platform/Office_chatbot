using Office.Application.Responders;

namespace Office.Application.Writing;

// v1.53 (B-34, layer 4, the owner: «اي احسن طريقه هتغطي كل الكيسز»): a free answer is read by a second AI call before it
// goes, for its MEANING, not for words: a promise the office cannot keep («مراتك مش هتعرف أبدًا»), legal information, a
// fact the office facts do not hold, or anything that harms the office. The word lists of ReplyGuard stay first; this one
// catches what no list can. Unsure, down or slow → the free answer never goes (its approved words do).

/// <summary>What the reviewer reads: the reply, the facts it may use, and the conversation it answers.</summary>
public sealed record ReviewRequest(string Reply, string Facts, IReadOnlyList<ChatLine> History);

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

    /// <summary>The reviewer could not answer (no key, down, slow, invalid): the free answer does not go.</summary>
    public const string Unavailable = "unavailable";

    public static readonly string[] Problems = [Promise, Legal, NotInFacts, Harmful];
}
