using System.Globalization;
using Office.Application.Booking;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Domain.Tasks;

namespace Office.Application.Responders;

/// <summary>
/// v1.47 (B-32): words typed while Hazem's buttons or list rows wait («بكره 10 وربع»، «اللي في المكتب»، «الأول») are the
/// client's choice when the AI reads them as one of them: the press they stand for, so every step that waits for a choice
/// answers typed words as it answers its buttons. The code only takes a choice that is really waiting.
/// </summary>
public static class TypedChoice
{
    /// <summary>The waiting choices as the AI reads them, numbered from 1, after the context (AiContext).</summary>
    public static IReadOnlyList<string> Titles(IReadOnlyList<InteractiveChoice> waiting) =>
        waiting.Select((c, i) => $"{i + 1}) {c.Title}{(c.Description is { Length: > 0 } d ? $" ({d})" : "")}").ToList();

    /// <summary>The waiting choice his words picked, as its press; null for none or a number outside the list.</summary>
    public static Interactive? Picked(IReadOnlyList<InteractiveChoice> waiting, RawClassification? ai) =>
        ai?.Picked is int n && n >= 1 && n <= waiting.Count ? As(waiting[n - 1]) : null;

    /// <summary>
    /// v1.65 (the owner's tests of 2026-11-05: «ولا حد فيهم» and even «مفيش حد منهم», the row's own words, were not taken as the
    /// row «مفيش حد منهم», and the client was handed to the office): words that ARE one of the waiting choices are that choice,
    /// by the code, before any AI reading: the choice's title said in full (a few words more are fine, emoji and spelling
    /// variants do not matter), or its number («2»، «رقم 2»، «التاني»). Never when his words say no («مش الأب»): then the AI
    /// reads them. Never a number when the choices are times (a bare «4» is an hour there). Null: no choice is meant for sure.
    /// </summary>
    /// <param name="guess">v1.79: false with understanding on: the shared-word guess is the AI's (it reads the chat).</param>
    public static Interactive? ByWords(IReadOnlyList<InteractiveChoice> waiting, string? words, bool guess = true)
    {
        if (words is null || waiting.Count == 0)
        {
            return null;
        }

        var said = Courtesy.Normalize(words);
        if (said.Length == 0 || said.Length > 60)
        {
            return null;
        }

        var isTimes = waiting.Any(c => BookingChoice.Parse(c.Id) is { Kind: 's' or 'n' or 't' or 'u' });
        if (!isTimes && OrdinalOf(said) is { } n && n >= 1 && n <= waiting.Count)
        {
            return As(waiting[n - 1]);
        }

        var hits = waiting.Where(c =>
        {
            var title = Courtesy.Normalize(c.Title);
            if (title.Length < 3)
            {
                return false;
            }

            // his words are the title (a few words more), or whole words of it («FaceTime» for «FaceTime (آيفون)»)
            return said == title || !SaysNo(said)
                && (said.Contains(title, StringComparison.Ordinal) && said.Length <= title.Length + 14
                    || said.Length >= 4 && $" {title} ".Contains($" {said} ", StringComparison.Ordinal));
        }).ToList();
        if (hits.Count == 1)
        {
            return As(hits[0]);
        }

        // v1.66: «ولا حد فيهم»، «محدش»، «مفيش» under a list that has a row for nobody
        if (hits.Count == 0 && NoneSaid.Contains(said))
        {
            var none = waiting.Where(c => { var t = Courtesy.Normalize(c.Title); return t.StartsWith("مفيش", StringComparison.Ordinal) || t.Contains("ولا حد", StringComparison.Ordinal)
                || t.Contains("لا حد", StringComparison.Ordinal) || t.Contains("ولا واحد", StringComparison.Ordinal); }).ToList();
            return none.Count == 1 ? As(none[0]) : null;
        }

        // v1.66: a short answer that shares a real word with ONE row only («موبايلي عادي» under «مكالمة عادية»): that row. Never
        // a question, never a refusal, never when two rows share a word.
        if (guess && hits.Count == 0 && said.Split(' ').Length <= 4 && !AsksQuestion(words, said) && !SaysNo(said))
        {
            var mine = Stems(said);
            if (mine.Count > 0)
            {
                var shared = waiting.Where(c => Stems(Courtesy.Normalize(c.Title)).Overlaps(mine)).ToList();
                if (shared.Count == 1)
                {
                    return As(shared[0]);
                }
            }
        }

        return null;
    }

    private static readonly HashSet<string> NoneSaid = new(StringComparer.Ordinal)
    {
        "ولا حد", "ولا حد فيهم", "ولا حد منهم", "لا حد", "لا حد فيهم", "لا حد منهم", "محدش", "محدش فيهم", "محدش منهم", "مفيش", "مفيش حد",
        "مفيش حد فيهم", "ولا واحد", "ولا واحد فيهم", "ولا واحد منهم", "لا ولا واحد", "لا مفيش حد",
    };

    private static readonly string[] QuestionWords = ["ايه", "بكام", "كام", "ازاي", "هل", "ليه", "فين", "امتي", "مين", "اي"];

    private static bool AsksQuestion(string words, string said) =>
        words.Contains('؟') || words.Contains('?') || said.Split(' ').Any(w => QuestionWords.Contains(w, StringComparer.Ordinal));

    private static readonly string[] Filler =
    [
        "عايز", "عاوز", "تمام", "ايوه", "اوكي", "طيب", "ماشي", "اختار", "حابب", "ممكن", "برضو", "بتاع", "بتاعي",
        // v1.68: the short words that say nothing about a choice («اول» does)
        "انا", "انت", "احنا", "هو", "هي", "ده", "دي", "دا", "كده", "بس", "اه", "لو", "في", "من", "علي", "مع", "عن", "اللي", "يعني",
        "ليها", "ليه", "ليا", "عندي", "عندها", "هيا", "هوا", "خلاص", "طب",
    ];

    /// <summary>The real words of a short text, each without «ال» and the endings «ه / ي / ات» (so «عادي» and «عادية» are one).</summary>
    private static HashSet<string> Stems(string normalized)
    {
        var stems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in normalized.Split(' '))
        {
            if (word.Length < 3 || Filler.Contains(word, StringComparer.Ordinal)) // v1.68: 3 letters too («اول»)
            {
                continue;
            }

            var w = word.StartsWith("ال", StringComparison.Ordinal) ? word[2..] : word;
            for (var i = 0; i < 2; i++)
            {
                if (w.EndsWith("ات", StringComparison.Ordinal) && w.Length > 4)
                {
                    w = w[..^2];
                }
                else if ((w.EndsWith('ه') || w.EndsWith('ي')) && w.Length > 3)
                {
                    w = w[..^1];
                }
            }

            if (w.Length >= 3)
            {
                stems.Add(w);
            }
        }

        return stems;
    }

    private static readonly string[] No = ["مش", "لا", "مفيش", "ولا", "غير", "بدل", "مو"];

    /// <summary>His words say no to something (a word of refusal as a whole word).</summary>
    private static bool SaysNo(string said) => said.Split(' ').Any(w => No.Contains(w, StringComparer.Ordinal));

    private static readonly (string Word, int N)[] Ordinals =
    [
        ("الاول", 1), ("الاولي", 1), ("اولا", 1), ("التاني", 2), ("الثاني", 2), ("التانيه", 2), ("الثانيه", 2),
        ("التالت", 3), ("الثالث", 3), ("التالته", 3), ("الثالثه", 3), ("الرابع", 4), ("الرابعه", 4),
    ];

    /// <summary>«2»، «رقم 2»، «التاني» (a single digit, or one ordinal word, alone).</summary>
    private static int? OrdinalOf(string said)
    {
        var s = said.StartsWith("رقم ", StringComparison.Ordinal) ? said[4..] : said;
        if (s.Length == 1 && char.IsDigit(s[0]))
        {
            return (int)char.GetNumericValue(s[0]);
        }

        foreach (var (word, n) in Ordinals)
        {
            if (s == word)
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>
    /// A list of times waits (to book, or to move an appointment) and his words asked for a day or a time on it: what the
    /// times are for, and the instant he asked. v1.65: an instant already past (today at 4 when it is 4:41) is asked as «the times
    /// from now», never lost: the list shows the next free ones.
    /// </summary>
    public static TimesAsked? Times(IReadOnlyList<InteractiveChoice> waiting, RawClassification? ai, DateTimeOffset now)
    {
        if (ai?.AskedTime is not { } asked || Local(asked) is not { } at)
        {
            return null;
        }

        var times = waiting.Select(c => BookingChoice.Parse(c.Id)).FirstOrDefault(p => p is { Kind: 's' or 'n' or 't' or 'u' });
        if (times is null)
        {
            return null;
        }

        if (at <= now)
        {
            return new TimesAsked(times, now, false, null, Passed: true, Asked: at);
        }

        var shown = waiting.FirstOrDefault(c => BookingChoice.Parse(c.Id) is { Kind: 's' or 't', At: { } t } && t == at);
        return new TimesAsked(times, at, ai.TimeExact == true, shown is null ? null : As(shown));
    }

    /// <summary>The press that books (or moves to) that instant.</summary>
    public static Interactive PickAt(BookingChoice times, DateTimeOffset at) => new(Reply, times.AppointmentId is { } id
        ? BookingChoice.MoveTo(id, at)
        : BookingChoice.Pick(times.Service!, at, times.App), null);

    /// <summary>The press of «مواعيد تانية» from that instant: the free times from it (it first, when it is free).</summary>
    public static Interactive TimesFrom(BookingChoice times, DateTimeOffset at) => new(Reply, times.AppointmentId is { } id
        ? BookingChoice.MoreMoves(id, at.AddSeconds(-1))
        : BookingChoice.More(times.Service!, at.AddSeconds(-1), times.App), null);

    private const string Reply = "list_reply";

    private static Interactive As(InteractiveChoice c) => new(Reply, c.Id, c.Title);

    /// <summary>«2026-11-02T10:15» in Cairo.</summary>
    private static DateTimeOffset? Local(string text) =>
        DateTime.TryParseExact(text.Trim(), ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local)
            ? new DateTimeOffset(local, OfficeHours.Cairo.GetUtcOffset(local))
            : null;
}

/// <summary>A day or a time asked while a list of times waits. <paramref name="Shown"/>: the row of that exact time, when shown.</summary>
/// v1.67: <paramref name="Passed"/>: he asked an instant already past (<paramref name="Asked"/>), so <paramref name="At"/> is now.
public sealed record TimesAsked(BookingChoice Times, DateTimeOffset At, bool Exact, Interactive? Shown, bool Passed = false, DateTimeOffset? Asked = null);
