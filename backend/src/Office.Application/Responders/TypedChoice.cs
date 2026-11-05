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
    public static Interactive? ByWords(IReadOnlyList<InteractiveChoice> waiting, string? words)
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
        return hits.Count == 1 ? As(hits[0]) : null;
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
            return new TimesAsked(times, now, false, null);
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
public sealed record TimesAsked(BookingChoice Times, DateTimeOffset At, bool Exact, Interactive? Shown);
