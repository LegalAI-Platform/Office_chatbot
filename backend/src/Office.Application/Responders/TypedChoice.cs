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
    /// A list of times waits (to book, or to move an appointment) and his words asked for a day or a time on it: what the
    /// times are for, and the instant he asked (in the future only).
    /// </summary>
    public static TimesAsked? Times(IReadOnlyList<InteractiveChoice> waiting, RawClassification? ai, DateTimeOffset now)
    {
        if (ai?.AskedTime is not { } asked || Local(asked) is not { } at || at <= now)
        {
            return null;
        }

        var times = waiting.Select(c => BookingChoice.Parse(c.Id)).FirstOrDefault(p => p is { Kind: 's' or 'n' or 't' or 'u' });
        if (times is null)
        {
            return null;
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
