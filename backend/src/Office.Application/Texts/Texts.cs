using System.Text.RegularExpressions;
using Office.Application.Turns;

namespace Office.Application.Texts;

/// <summary>A row of hazem.approved_texts. Every word Hazem sends comes from one of these (T-07).</summary>
public sealed record ApprovedText(string Key, int Version, string Text, int MaxChars, bool Active);

public interface IApprovedTexts
{
    /// <summary>The active version of every key.</summary>
    Task<IReadOnlyDictionary<string, ApprovedText>> GetActiveAsync(CancellationToken ct);
}

/// <summary>The keys Hazem uses in TASK 04 (seeded by migration 0005; docs/approved-texts-proposal.md).</summary>
public static class TextKeys
{
    // From DECISIONS (v1.4, unchanged in v1.5), word for word (active).
    public const string WelcomeAskName = "welcome_ask_name";
    public const string ConsultationOffer = "consultation_offer";
    public const string LegalQuestion = "legal_question";
    public const string UrgentPrice = "urgent_price";
    public const string DocumentReviewRequest = "document_review_request";
    public const string WrittenOpinion = "written_opinion";
    public const string UrfUnclear = "urf_unclear";
    public const string UrfProof = "urf_proof";

    // Proposed, waiting for the owner (inactive until approved).
    public const string AskNameAgain = "ask_name_again";
    public const string Unclear = "unclear";
    public const string UnclearAgain = "unclear_again";
    public const string Greeting = "greeting";
    public const string Thanks = "thanks";
    public const string HumanRequest = "human_request";
    public const string ExistingMatter = "existing_matter";
    public const string RescheduleOrCancel = "reschedule_or_cancel";
    public const string ContractDraftingRequest = "contract_drafting_request";
    public const string FormalNoticeRequest = "formal_notice_request";
    public const string UrfRequest = "urf_request";
    public const string PriceDocumentReview = "price_document_review";
    public const string PriceContractDrafting = "price_contract_drafting";
    public const string PriceFormalNotice = "price_formal_notice";
    public const string PriceUrf = "price_urf";
    public const string PriceCaseRepresentation = "price_case_representation";
    public const string MediaReceived = "media_received";
    public const string MediaTooLarge = "media_too_large";
    public const string MediaFailed = "media_failed";
    public const string MediaRejected = "media_rejected";
    public const string VoiceFailed = "voice_failed";
    public const string Unsupported = "unsupported";
    public const string LocationReceived = "location_received";
}

/// <summary>One reply Hazem wants to send: a text key, and what to send instead when it cannot be sent.</summary>
public sealed record ReplyStep(string Key, string? Fallback = null, IReadOnlyDictionary<string, string>? Fill = null);

/// <summary>Which approved text (key and version) went out under which idempotency key, or why another one did.</summary>
public sealed record ReplyRecord(string IdempotencyKey, string Key, int Version, int Part, string? InsteadOf, string? Reason);

/// <summary>A text that could not be sent and why (logged by key, never by content).</summary>
public sealed record SkippedText(string Key, string Reason);

public static partial class TextRenderer
{
    public const string Inactive = "inactive_or_missing";
    public const string MissingValue = "missing_value";

    /// <summary>
    /// Fills {{lower_case}} slots from <paramref name="fill"/>. Any slot left — and any DECISIONS
    /// part-5 placeholder such as {{OFFICE_ADDRESS}} or a [bracket] slot — means the text cannot
    /// go out (DECISIONS part 5). Returns null and the reason in that case.
    /// </summary>
    public static (string? Text, string? Reason) Render(ApprovedText text, IReadOnlyDictionary<string, string>? fill)
    {
        var rendered = Slot().Replace(text.Text, m =>
            fill is not null && fill.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        return HasOpenSlot(rendered) ? (null, MissingValue) : (rendered, null);
    }

    /// <summary>{{anything}} or [anything]: a value nobody filled (healthcheck and tests use it too).</summary>
    public static bool HasOpenSlot(string text) => OpenSlot().IsMatch(text);

    /// <summary>
    /// B-21: a text longer than its limit is cut at a natural point — the end of a sentence,
    /// else a line, else a space — never in the middle of a word.
    /// </summary>
    public static IReadOnlyList<string> Split(string text, int maxChars)
    {
        var parts = new List<string>();
        var rest = text.Trim();
        while (rest.Length > maxChars)
        {
            var window = rest[..(maxChars + 1)];
            var cut = LastBreak(window, ['.', '؟', '?', '!', '؛']) is var s and > 0 ? s + 1
                : window.LastIndexOf('\n') is var n and > 0 ? n
                : window.LastIndexOf(' ') is var w and > 0 ? w
                : maxChars;
            parts.Add(rest[..cut].Trim());
            rest = rest[cut..].Trim();
        }

        if (rest.Length > 0)
        {
            parts.Add(rest);
        }

        return parts;
    }

    private static int LastBreak(string window, char[] marks)
    {
        var best = -1;
        foreach (var c in marks)
        {
            best = Math.Max(best, window[..^1].LastIndexOf(c));
        }

        return best;
    }

    [GeneratedRegex(@"\{\{([a-z_]+)\}\}")]
    private static partial Regex Slot();

    [GeneratedRegex(@"\{\{[^}]*\}\}|\[[^\]]*\]")]
    private static partial Regex OpenSlot();
}

/// <summary>Turns reply steps into n8n actions, with one stable idempotency key per part.</summary>
public static class ReplyComposer
{
    public sealed record Composed(IReadOnlyList<OutboundAction> Actions, IReadOnlyList<ReplyRecord> Records, IReadOnlyList<SkippedText> Skipped);

    public static Composed Compose(string turnId, string? contextWamid, IEnumerable<ReplyStep> steps,
        IReadOnlyDictionary<string, ApprovedText> texts)
    {
        var actions = new List<OutboundAction>();
        var records = new List<ReplyRecord>();
        var skipped = new List<SkippedText>();
        var sent = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in steps)
        {
            var (chosen, rendered, reason) = Resolve(step.Key, step.Fill, texts);
            string? insteadOf = null;
            if (chosen is null)
            {
                skipped.Add(new SkippedText(step.Key, reason!));
                if (step.Fallback is null)
                {
                    continue;
                }

                insteadOf = step.Key;
                (chosen, rendered, var fallbackReason) = Resolve(step.Fallback, step.Fill, texts);
                if (chosen is null)
                {
                    skipped.Add(new SkippedText(step.Fallback, fallbackReason!));
                    continue;
                }
            }

            if (!sent.Add(chosen.Key))
            {
                continue; // the same text twice in one answer (e.g. a fallback equal to the next step)
            }

            var parts = TextRenderer.Split(rendered!, chosen.MaxChars);
            for (var p = 0; p < parts.Count; p++)
            {
                var key = OutboundAction.KeyFor(turnId, actions.Count);
                actions.Add(new OutboundAction(ActionType.SendText, key, Text: parts[p], ContextWamid: actions.Count == 0 ? contextWamid : null));
                records.Add(new ReplyRecord(key, chosen.Key, chosen.Version, p, insteadOf, insteadOf is null ? null : reason));
            }
        }

        return new Composed(actions, records, skipped);
    }

    private static (ApprovedText? Text, string? Rendered, string? Reason) Resolve(string key,
        IReadOnlyDictionary<string, string>? fill, IReadOnlyDictionary<string, ApprovedText> texts)
    {
        if (!texts.TryGetValue(key, out var text) || !text.Active)
        {
            return (null, null, TextRenderer.Inactive);
        }

        var (rendered, reason) = TextRenderer.Render(text, fill);
        return rendered is null ? (null, null, reason) : (text, rendered, null);
    }
}
