using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Office.Application.Texts;

namespace Office.Application.Understanding;

// DECISIONS v1.21 (B-01.6): the greetings and the courtesies a client writes are answered in kind, read by code (never
// the AI): «السلام عليكم» gets «وعليكم السلام…» before Hazem's answer, «صباح الخير» «صباح النور», and so on. A message
// that is only a goodbye gets the goodbye alone. The thanks stay the classifier's (thanks_or_closing, B-01).

/// <summary>What the client's greeting asks back, in the order Hazem answers one (the first that matches).</summary>
public enum CourtesyKind
{
    None,
    Salam,
    Ramadan,
    Jumua,
    Occasion,
    Morning,
    Evening,
    HowAreYou,
    GoodNight,
    Goodbye,
}

/// <summary>The greeting found in a message, and whether the message holds nothing else.</summary>
public sealed record CourtesyMatch(CourtesyKind Kind, bool Only)
{
    public static readonly CourtesyMatch NoneFound = new(CourtesyKind.None, false);

    /// <summary>The approved text that answers it (none for <see cref="CourtesyKind.None"/>).</summary>
    public string? TextKey => Kind switch
    {
        CourtesyKind.Salam => TextKeys.GreetSalam,
        CourtesyKind.Ramadan => TextKeys.GreetRamadan,
        CourtesyKind.Jumua => TextKeys.GreetJumua,
        CourtesyKind.Occasion => TextKeys.GreetOccasion,
        CourtesyKind.Morning => TextKeys.GreetMorning,
        CourtesyKind.Evening => TextKeys.GreetEvening,
        CourtesyKind.HowAreYou => TextKeys.GreetHowAreYou,
        CourtesyKind.GoodNight => TextKeys.GreetGoodNight,
        CourtesyKind.Goodbye => TextKeys.GreetGoodbye,
        _ => null,
    };

    /// <summary>A goodbye with nothing else: answered alone, without the AI or the rest of the flow.</summary>
    public bool EndsTheChat => Only && Kind is CourtesyKind.Goodbye or CourtesyKind.GoodNight;
}

public static partial class Courtesy
{
    /// <summary>The greetings, on the normalized text (no diacritics; ا for أإآ, ي for ى, ه for ة; Latin in lower case).</summary>
    private static readonly (CourtesyKind Kind, Regex Pattern)[] Greetings =
    [
        (CourtesyKind.Salam, SalamRegex()),
        (CourtesyKind.Ramadan, RamadanRegex()),
        (CourtesyKind.Jumua, JumuaRegex()),
        (CourtesyKind.Occasion, OccasionRegex()),
        (CourtesyKind.Morning, MorningRegex()),
        (CourtesyKind.Evening, EveningRegex()),
        (CourtesyKind.HowAreYou, HowAreYouRegex()),
        (CourtesyKind.GoodNight, GoodNightRegex()),
        (CourtesyKind.Goodbye, GoodbyeRegex()),
    ];

    /// <summary>The first greeting of the message (in the order above), or <see cref="CourtesyMatch.NoneFound"/>.</summary>
    public static CourtesyMatch Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500)
        {
            return CourtesyMatch.NoneFound;
        }

        var t = Normalize(text);
        var kind = CourtesyKind.None;
        var rest = t;
        foreach (var (k, pattern) in Greetings)
        {
            if (!pattern.IsMatch(t))
            {
                continue;
            }

            if (kind == CourtesyKind.None)
            {
                kind = k;
            }

            rest = pattern.Replace(rest, " ");
        }

        if (kind == CourtesyKind.None)
        {
            return CourtesyMatch.NoneFound;
        }

        // Only words of address and filler left («يا فندم», «يا حازم», «حضرتك») = the message is the greeting alone.
        rest = FillerRegex().Replace(rest, " ");
        return new CourtesyMatch(kind, !rest.Any(char.IsLetterOrDigit));
    }

    /// <summary>Lower case Latin, no Arabic diacritics or tatweel, the usual letter variants unified, letters repeated 3+ times
    /// once («شكراااا»), and anything but letters and digits as one space.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture))
        {
            if (ch is >= 'ً' and <= 'ْ' or 'ٰ' or 'ـ')
            {
                continue; // tashkeel, the dagger alif, tatweel
            }

            var c = ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => ch,
            };
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        var s = RepeatRegex().Replace(sb.ToString(), "$1");
        return SpacesRegex().Replace(s, " ").Trim();
    }

    // Every pattern is bounded by spaces it does not take ((?<!\S) … (?!\S)), so two greetings side by side both match.

    // «السلام عليكم» and its spellings: «سلام عليكم»، «سلامو عليكو»، «… ورحمه الله وبركاته»، «سلام» alone; Latin «salam alaikum».
    [GeneratedRegex(@"(?<!\S)(?:(?:ال)?سلامو?(?: (?:علي|ع)(?:كم|كو|كوم|ك|كي|كمو))?(?: ورحمه الله(?: وبركاته)?)?|(?:a?s+alam[uo]?|slam)(?: [a3]?l[aei]?[iy]?k[uo]?m)?)(?!\S)")]
    private static partial Regex SalamRegex();

    // «رمضان كريم» (answered «الله أكرم»).
    [GeneratedRegex(@"(?<!\S)(?:رمضان (?:كريم|مبارك)|ramadh?an (?:kareem|karim|mubarak))(?!\S)")]
    private static partial Regex RamadanRegex();

    // «جمعة مباركة» (answered «علينا وعليكم»).
    [GeneratedRegex(@"(?<!\S)(?:جمعه مباركه|(?:jumaa|jumma|gom3a) mubaraka?)(?!\S)")]
    private static partial Regex JumuaRegex();

    // «كل سنة وانت طيب»، «عيد سعيد / مبارك»، «سنة سعيدة».
    [GeneratedRegex(@"(?<!\S)(?:كل (?:سنه|عام) و(?:انت|انتي|انتو|انتوا|انتم|حضرتك) (?:طيب|طيبه|طيبين|بخير)|عيد (?:سعيد|مبارك)|(?:عيدكم|عيدك) مبارك|عيد (?:اضحي|فطر) مبارك|سنه (?:جديده )?سعيده|(?:happy|blessed) (?:eid|new year)|eid mubarak)(?!\S)")]
    private static partial Regex OccasionRegex();

    // «صباح الخير / الفل / الورد…»; Latin.
    [GeneratedRegex(@"(?<!\S)(?:صباح (?:الخير|الفل|الورد|النور|الياسمين|العسل|الجمال)|(?:good )?morning|sabah el ?(?:kheir|khair|5eir|5er))(?!\S)")]
    private static partial Regex MorningRegex();

    // «مساء الخير / الفل…» (also «مسا»); Latin.
    [GeneratedRegex(@"(?<!\S)(?:مساء? (?:الخير|الفل|الورد|النور|الياسمين|العسل|الجمال)|good (?:evening|afternoon)|masa el ?(?:kheir|khair|5eir|5er))(?!\S)")]
    private static partial Regex EveningRegex();

    // «إزيك»، «عامل إيه»، «أخبارك»…
    [GeneratedRegex(@"(?<!\S)(?:ازيك|ازيكم|ازيكو|ازيكي|ازي حضرتك|ازاي حضرتك|عامل ايه|عامله ايه|عاملين ايه|عامل اي|اخبارك|اخبار حضرتك|ايه الاخبار|كيفك|كيف حالك|كيف الحال|how are (?:you|u)|ezayak|ezayk|3amel eh)(?!\S)")]
    private static partial Regex HowAreYouRegex();

    // «تصبح على خير».
    [GeneratedRegex(@"(?<!\S)(?:تصبح(?:وا|ي)? علي خير|good night)(?!\S)")]
    private static partial Regex GoodNightRegex();

    // «مع السلامة»، «باي»، «في رعاية الله».
    [GeneratedRegex(@"(?<!\S)(?:مع السلامه|باي(?: باي)?|في رعايه الله|في امان الله|bye(?: bye)?|good ?bye)(?!\S)")]
    private static partial Regex GoodbyeRegex();

    // Words of address and filler that make no request: «يا فندم»، «يا حازم»، «حضرتك»، «أهلًا»، «هاي»…
    [GeneratedRegex(@"(?<!\S)(?:يا|يافندم|فندم|حازم|باشا|استاذ|استاذنا|جماعه|حضرتك|اهلا|اهلين|سهلا|وسهلا|هاي|هلا|مرحبا|hi|hello|hey|و|الله|تحياتي|عليكم|عليكو|ورحمه|وبركاته)(?!\S)")]
    private static partial Regex FillerRegex();

    [GeneratedRegex(@"(\p{L})\1{2,}")]
    private static partial Regex RepeatRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
