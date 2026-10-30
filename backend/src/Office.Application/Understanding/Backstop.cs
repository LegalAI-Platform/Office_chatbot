using System.Text.RegularExpressions;

namespace Office.Application.Understanding;

// The owner (2026-10-30, migration 0032): when the classifier gives no answer at all (no key, an error, a timeout, an
// answer it could not read), two things are still read by code, so the AI being down never turns them into the
// «unclear» answer: an insult (S-01: the warning once, then silence) and a message that is only a thanks (B-01). Nothing
// else is guessed: every other message keeps the fallback, as before. A low confidence is the AI's own answer and is
// left alone.

/// <summary>Reads an insult or a plain thanks by code when the AI could not read the message.</summary>
public static partial class Backstop
{
    public const string Prefix = "backstop:";

    /// <summary>
    /// <paramref name="c"/> as it is, unless the AI failed and <paramref name="text"/> is an insult or only a thanks:
    /// then that intent, with the reason kept as <c>backstop:&lt;reason&gt;</c> for the classification log.
    /// </summary>
    public static Classification Apply(Classification c, string? text)
    {
        if (c.FallbackReason is not (ClassifierOutcome.NoKey or ClassifierOutcome.Timeout or ClassifierOutcome.Error
            or ClassifierOutcome.InvalidOutput) || string.IsNullOrWhiteSpace(text) || text.Length > 500)
        {
            return c;
        }

        var t = Courtesy.Normalize(text);
        var intent = IsInsult(t) ? Intent.Abusive : IsOnlyThanks(t) ? Intent.ThanksOrClosing : (Intent?)null;
        return intent is { } i ? c with { Intent = i, FallbackReason = null, BackstopFor = c.FallbackReason } : c;
    }

    /// <summary>On the normalized text (<see cref="Courtesy.Normalize"/>).</summary>
    public static bool IsInsult(string t) => InsultRegex().IsMatch(t);

    /// <summary>Only thanks words (and «ليك»، «جدا»، «يا أستاذ»…): «شكرا ليك»، «متشكر جدا»، «تسلم».</summary>
    public static bool IsOnlyThanks(string t) =>
        ThanksRegex().IsMatch(t) && string.IsNullOrWhiteSpace(FillerRegex().Replace(ThanksRegex().Replace(t, " "), " "));

    // Egyptian and common insults, as Courtesy.Normalize writes them (ا for أإآ, ه for ة, ي for ى, Latin lower case).
    // A root starts a word (نيك in «انيكك» but not in «تكنيك»); short ones are whole words (زب, not «زبون»).
    [GeneratedRegex(@"(?<!\S)(?:[اهبتنحي]?نيك\S*|من(?:ي|يو)ك\S*|متناك\S*|اتناك\S*|كسم\S*|كس (?:ام|امك|اختك|اهلك)\S*|شرموط\S*|معرص\S*|عرص(?:ه)?|خول|لبوه|قحبه|زب(?:ي|ر|ك)?|طيز\S*|يلعن (?:ابو|دين|امك)\S*|دين (?:امك|ابوك|اهلك)|ابن (?:ال)?(?:متناكه|شرموطه|وسخه|كلب)|f+u+c+k\S*|motherf\S*|bitch\S*|ass ?hole\S*|dick\S*|pussy\S*)(?!\S)")]
    private static partial Regex InsultRegex();

    [GeneratedRegex(@"(?<!\S)(?:الف شكر|شكرا|شكر|متشكرين|متشكره|متشكر|تسلم ايديك|تسلم ايدك|تسلموا|تسلمي|تسلم|مرسي|ميرسي|جزاك الله خير\S*|ربنا يخليك|ربنا يكرمك|thanks?|thank you|thx|merci)(?!\S)")]
    private static partial Regex ThanksRegex();

    [GeneratedRegex(@"(?<!\S)(?:ليك|ليكي|ليكم|لك|ليكو|جدا|اوي|كتير|خالص|حضرتك|يا \S+|يا|و|انت|استاذ|استاذنا|حازم|تمام|ماشي|اوكي|ok|okay|كده|بجد|[.!،,؟?🙏❤️👍🌹😊]+)(?!\S)")]
    private static partial Regex FillerRegex();
}
