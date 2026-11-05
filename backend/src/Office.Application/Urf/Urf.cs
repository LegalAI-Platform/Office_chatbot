using System.Globalization;
using Office.Application.Booking;
using Office.Application.Services;
using Office.Application.Understanding;
using Office.Application.Texts;

namespace Office.Application.Urf;

// TASK 11 (DECISIONS v1.15, B-11 / S-06): the Urfi contract and divorce from WhatsApp. One question per message in
// B-11's order; the closed questions are buttons (read without the AI), the ages are typed and read here. The decisions
// that refuse a client (the age, the guardian) are kept with PolicyVersion.

/// <summary>An Urfi case (S-06). Kind: marriage or divorce; Step: the question waiting for its answer.</summary>
public sealed record UrfCase(
    Guid Id, Guid PersonId, string Kind, string State, string? Step,
    int? HusbandAge = null, int? WifeAge = null, string? WifePast = null, string? Guardian = null, string? Witnesses = null,
    bool? HasCopy = null, bool? Agreed = null, bool? Children = null, int AgeTries = 0, Guid? AppointmentId = null,
    string? PolicyVersion = null)
{
    public const string Marriage = "marriage";
    public const string Divorce = "divorce";

    public const string Collecting = "collecting";
    public const string RejectedAge = "rejected_age";
    public const string RejectedGuardian = "rejected_guardian";
    public const string Redirected = "redirected_consultation";
    public const string DocumentsPending = "documents_pending";
    public const string Booked = "booked";
    public const string LawyerRejected = "lawyer_rejected";
    public const string Approved = "approved";

    /// <summary>v1.63: collecting or waiting for a time, never held, and left alone for the fresh days (the worker closes it).</summary>
    public const string Abandoned = "abandoned";

    public static UrfCase Start(Guid personId, string kind) =>
        new(Guid.NewGuid(), personId, kind, Collecting, kind == Marriage ? UrfStep.Ages : UrfStep.HasCopy);

    public bool IsMarriage => Kind == Marriage;

    /// <summary>The booking it leads to: the contract or the divorce, with the office's witnesses (5,000) or theirs (4,000).</summary>
    public BookingService Service => BookingService.Urf(IsMarriage, Witnesses == "office");

    /// <summary>v1.68 (the owner): the guardian is asked, and comes, only when it is her first marriage.</summary>
    public bool NeedsGuardian => IsMarriage && WifePast == "first";

    /// <summary>B-11.5: what to send, from the answers. v1.68: one paper for a divorce or a khul, the death certificate for a widow.</summary>
    public string Documents()
    {
        var items = IsMarriage
            ? new List<string> { "بطاقة الزوج", "بطاقة الزوجة" }
            : ["نسخة العقد العرفي", "بطاقة الزوج", "بطاقة الزوجة"];
        if (NeedsGuardian && GuardianName(Guardian) is { Length: > 0 } guardian)
        {
            items.Add($"بطاقة الولي ({guardian})");
        }

        if (WifePast is "divorced" or "khul")
        {
            items.Add("مستند الطلاق أو حكم الخلع");
        }
        else if (WifePast == "widow")
        {
            items.Add("شهادة وفاة الزوج السابق");
        }

        if (Witnesses == "own")
        {
            items.Add("بطاقتين الشاهدين");
        }

        return string.Join("، و", items);
    }

    /// <summary>B-11.7: who comes to the office.</summary>
    public string Attendees() => AttendeesFor(Kind, Witnesses, NeedsGuardian ? Guardian : null);

    /// <summary>B-11.7 for a case read elsewhere (TASK 12: the reminder of its appointment).</summary>
    /// <param name="guardian">v1.68: the guardian who comes (a first marriage only); none: «الزوج والزوجة».</param>
    public static string AttendeesFor(string kind, string? witnesses, string? guardian) =>
        (kind == Marriage ? guardian is "father" or "brother" or "uncle" ? "الزوج والزوجة والولي" : "الزوج والزوجة" : "الطرفين")
        + (witnesses == "own" ? " والشاهدين" : "");

    public static string GuardianName(string? guardian) => guardian switch
    {
        "father" => "الأب",
        "brother" => "الأخ",
        "uncle" => "العم",
        _ => "",
    };
}

public static class UrfStep
{
    public const string Ages = "ages";
    public const string WifePast = "wife_past";
    public const string Guardian = "guardian";
    public const string Witnesses = "witnesses";
    public const string HasCopy = "has_copy";
    public const string Agreed = "agreed";
    public const string Children = "children";
}

/// <summary>What Hazem does after an answer.</summary>
public enum UrfNext
{
    /// <summary>Ask the case's <see cref="UrfCase.Step"/> (the next question, or the same one again).</summary>
    Ask,
    AskAgesAgain,
    /// <summary>The ages could not be read twice: the office takes it from here.</summary>
    Help,
    RejectAge,
    RejectGuardian,
    /// <summary>B-11 divorce: no copy, not agreed or children: the consultation offer only.</summary>
    Redirect,
    /// <summary>All answered: the price, the documents and the free times.</summary>
    Book,
}

/// <summary>A button Hazem sent for a question: <c>urf:&lt;step&gt;:&lt;value&gt;</c>.</summary>
public sealed record UrfAnswer(string Step, string Value)
{
    public const string Prefix = "urf:";

    public static string Id(string step, string value) => $"{Prefix}{step}:{value}";

    public static UrfAnswer? Parse(string? id)
    {
        if (id is null || !id.StartsWith(Prefix, StringComparison.Ordinal) || id.Length > 60)
        {
            return null;
        }

        var p = id.Split(':');
        return p.Length == 3 ? new UrfAnswer(p[1], p[2]) : null;
    }
}

/// <summary>
/// v1.20 (B-11.1): the client's last age refusal, how many closing texts followed it, and whether Hazem has answered
/// anything else since (the client moved on: thanks, a booking, another matter…).
/// </summary>
/// <summary>
/// v1.20 (B-11.1) the age refusal; v1.71 any Urfi refusal of the client (the age, or no guardian for a first marriage), with
/// the closing text that answers the same matter after it (<see cref="ClosingKey"/>).
/// </summary>
public sealed record AgeRefusal(DateTimeOffset At, int ClosingReplies, bool MovedOn = false, string State = UrfCase.RejectedAge)
{
    public string ClosingKey => State == UrfCase.RejectedGuardian ? TextKeys.UrfGuardianClosed : TextKeys.UrfAgeClosed;
}

public static class UrfRules
{
    /// <summary>B-11: every decision that refuses a client (the age, the guardian) or the lawyer's is kept with this.</summary>
    public const string PolicyVersion = "B-11 v1.15";

    /// <summary>v1.20 (B-11.1): how long after an age refusal the same matter gets the closing text, and how often.</summary>
    public static readonly TimeSpan AgeRefusalWindow = TimeSpan.FromHours(24);

    public const int MaxClosingReplies = 2;

    /// <summary>
    /// v1.20 (B-11.1): a message after an age refusal that is about the same marriage: a question or a new request named
    /// as a marriage (or an Urfi one) for 24 hours; a message with no topic at all («ليه», «طب عايز استشارة») only while
    /// the client has not moved on, so «موعد صلاة الظهر» after a thanks is answered as any message. Another matter (a
    /// divorce, an inheritance…), a person, the office's details or thanks go on as usual.
    /// </summary>
    public static bool FollowsAgeRefusal(Classification c, AgeRefusal r)
    {
        if (c.Intent is not (Intent.Other or Intent.LegalQuestion or Intent.DescribeMatter or Intent.ConsultationRequest
            or Intent.PriceQuestion or Intent.ServiceByName))
        {
            return false;
        }

        var named = c.Service is Service.UrfMarriage or Service.UrfUnclear
                    || c.Matter == MatterType.Urfi && c.Subtype is null or MatterSubtype.UrfMarriage
                    || c.Matter == MatterType.PersonalStatus && c.Subtype == MatterSubtype.Marriage;
        var vague = c.Service is null && (c.Matter is null || c.Matter == MatterType.PersonalStatus && c.Subtype is null);
        return named || vague && !r.MovedOn;
    }

    public const int MinAge = 18;

    /// <summary>The answer to the case's waiting question (an answer to another question just asks it again).</summary>
    public static (UrfCase Case, UrfNext Next) Answer(UrfCase c, UrfAnswer a)
    {
        if (c.State != UrfCase.Collecting || a.Step != c.Step)
        {
            return (c, UrfNext.Ask);
        }

        return (a.Step, a.Value) switch
        {
            (UrfStep.WifePast, "first" or "divorced" or "khul" or "widow") => Advance(c with { WifePast = a.Value }),
            (UrfStep.Guardian, "none") => (c with { Guardian = "none", State = UrfCase.RejectedGuardian, Step = null, PolicyVersion = PolicyVersion },
                UrfNext.RejectGuardian),
            (UrfStep.Guardian, "father" or "brother" or "uncle") => Advance(c with { Guardian = a.Value }),
            (UrfStep.Witnesses, "office" or "own") => (c with { Witnesses = a.Value, State = UrfCase.DocumentsPending, Step = null }, UrfNext.Book),
            (UrfStep.HasCopy, "yes") => Advance(c with { HasCopy = true }),
            (UrfStep.HasCopy, "no") => (Redirect(c with { HasCopy = false }), UrfNext.Redirect),
            (UrfStep.Agreed, "yes") => Advance(c with { Agreed = true }),
            (UrfStep.Agreed, "no") => (Redirect(c with { Agreed = false }), UrfNext.Redirect),
            (UrfStep.Children, "no") => Advance(c with { Children = false }),
            (UrfStep.Children, "yes") => (Redirect(c with { Children = true }), UrfNext.Redirect),
            _ => (c, UrfNext.Ask),
        };
    }

    /// <summary>
    /// v1.62 (the owner's chat: he paid for the office's witnesses, then wrote «الشهود هجيبهم من عندي»): a fact that DIFFERS from the
    /// case's answer when the case already has its time held or confirmed. Nothing changes by itself then (the price paid, the
    /// documents, a refund are the office's decision): the office is told.
    /// </summary>
    public static bool ChangesBooked(UrfCase c, string? wifePast, string? guardian, string? witnesses)
    {
        var locked = c.State is UrfCase.Booked or UrfCase.Approved || c.State == UrfCase.DocumentsPending && c.AppointmentId is not null;
        static bool Differs(string? now, string? said, params string[] allowed) => said is not null && allowed.Contains(said) && said != now;
        return locked && (Differs(c.Witnesses, witnesses, "office", "own")
            || c.IsMarriage && (Differs(c.WifePast, wifePast, "first", "divorced", "khul", "widow") || Differs(c.Guardian, guardian, "father", "brother", "uncle")));
    }

    /// <summary>B-11.1: the typed ages. Under 18 on either side closes the case without a price or a time.</summary>
    /// <param name="words">v1.52 (phase 1): numbers in words («الاتنين تلاتين») are read too.</param>
    public static (UrfCase Case, UrfNext Next) Ages(UrfCase c, string? text, bool words = false)
    {
        if (c.State != UrfCase.Collecting || c.Step != UrfStep.Ages)
        {
            return (c, UrfNext.Ask);
        }

        if (UrfAges.Parse(words && text is not null ? NumberWords.ToDigits(text) : text) is not { } ages)
        {
            var tried = c with { AgeTries = c.AgeTries + 1 };
            return (tried, tried.AgeTries >= 2 ? UrfNext.Help : UrfNext.AskAgesAgain);
        }

        var (husband, wife) = ages;
        var answered = c with { HusbandAge = husband, WifeAge = wife };
        return husband < MinAge || wife < MinAge
            ? (answered with { State = UrfCase.RejectedAge, Step = null, PolicyVersion = PolicyVersion }, UrfNext.RejectAge)
            : Advance(answered);
    }

    /// <summary>
    /// v1.60: the next question the case still needs, in B-11's order, skipping what is already known (said in his own words
    /// before it was asked). Null: everything is known, the price and times can go.
    /// </summary>
    public static string? NextStep(UrfCase c) => c.IsMarriage
        ? c.HusbandAge is null ? UrfStep.Ages
            : c.WifePast is null ? UrfStep.WifePast
            : c.WifePast == "first" && c.Guardian is null ? UrfStep.Guardian // v1.68: a first marriage only
            : c.Witnesses is null ? UrfStep.Witnesses : null
        : c.HasCopy is null ? UrfStep.HasCopy
            : c.Agreed is null ? UrfStep.Agreed
            : c.Children is null ? UrfStep.Children
            : c.Witnesses is null ? UrfStep.Witnesses : null;

    private static (UrfCase Case, UrfNext Next) Advance(UrfCase c) =>
        NextStep(c) is { } step
            ? (c with { Step = step }, UrfNext.Ask)
            : (c with { State = UrfCase.DocumentsPending, Step = null }, UrfNext.Book);

    /// <summary>
    /// v1.60 (the owner: «قال انها مطلقه ليه البوت ساله بعد كدا؟» and «عايز الشهود من المكتب» got the price of his own
    /// witnesses): what the client said in his own words. While the questions are collected a fact fills an answer not given yet
    /// (never one already given), and the questions it answers are skipped; while the price and times wait for a pick, a fact
    /// that differs from the answer given CHANGES it, so the price and documents are worked out again. A case with a time held,
    /// or closed, is the office's. Values outside the lists are ignored.
    /// </summary>
    public static UrfCase ApplyFacts(UrfCase c, string? wifePast, string? guardian, string? witnesses)
    {
        static string? Only(string? v, params string[] allowed) => v is not null && allowed.Contains(v) ? v : null;
        var past = c.IsMarriage ? Only(wifePast, "first", "divorced", "khul", "widow") : null;
        var guard = c.IsMarriage ? Only(guardian, "father", "brother", "uncle") : null;
        var wit = Only(witnesses, "office", "own");
        if (c.State == UrfCase.Collecting)
        {
            var filled = c with { WifePast = c.WifePast ?? past, Guardian = c.Guardian ?? guard, Witnesses = c.Witnesses ?? wit };
            if (filled == c)
            {
                return c;
            }

            return NextStep(filled) is { } step
                ? filled with { Step = step }
                : filled with { State = UrfCase.DocumentsPending, Step = null };
        }

        return c.State == UrfCase.DocumentsPending && c.AppointmentId is null
            ? c with { WifePast = past ?? c.WifePast, Guardian = guard ?? c.Guardian, Witnesses = wit ?? c.Witnesses }
            : c;
    }

    private static UrfCase Redirect(UrfCase c) => c with { State = UrfCase.Redirected, Step = null };
}

/// <summary>«30 و 25», «الزوج 30 والزوجة ٢٥»: exactly two whole numbers (Arabic or Latin digits), the husband first.</summary>
public static class UrfAges
{
    public static (int Husband, int Wife)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200)
        {
            return null;
        }

        var numbers = new List<int>();
        var roles = new List<char?>(); // v1.71: 'h' or 'w' from the word said before each number, if any
        char? said = null;
        var digits = new System.Text.StringBuilder();
        var word = new System.Text.StringBuilder();
        foreach (var ch in text + " ")
        {
            var d = CharUnicodeInfo.GetDecimalDigitValue(ch);
            if (d >= 0)
            {
                digits.Append((char)('0' + d));
                continue;
            }

            if (digits.Length > 0)
            {
                if (digits.Length > 3)
                {
                    return null;
                }

                numbers.Add(int.Parse(digits.ToString(), CultureInfo.InvariantCulture));
                roles.Add(said);
                said = null;
                digits.Clear();
            }

            if (char.IsLetter(ch))
            {
                word.Append(ch);
                continue;
            }

            if (word.Length > 0)
            {
                said = RoleOf(word.ToString()) ?? said;
                word.Clear();
            }
        }

        // v1.71 (Ahmed Amin's chat: «الزوجه 20 سنه وانا 40 سنه» was read as the husband 20): a number after «الزوجة / مراتي /
        // هي» is hers, after «الزوج / انا / هو» his; one named, the other is the other one. None named: the husband first.
        if (numbers.Count == 2)
        {
            var (first, second) = (roles[0], roles[1]);
            if (first == 'w' && second != 'w' || second == 'h' && first != 'h')
            {
                (numbers[0], numbers[1]) = (numbers[1], numbers[0]);
            }
        }

        // v1.22: one age said for both («الاتنين 20»، «كل واحد 25»، «زي بعض 30»).
        if (numbers is [var both] && BothWords.Any(w => $" {Courtesy.Normalize(text)} ".Contains(w, StringComparison.Ordinal)))
        {
            numbers.Add(both);
        }

        return numbers is [var h, var w] && h is >= 1 and <= 120 && w is >= 1 and <= 120 ? (h, w) : null;
    }

    /// <summary>On the normalized text (Courtesy.Normalize), with the spaces around.</summary>
    private static readonly string[] BothWords = [" الاتنين ", " الاثنين ", " التنين ", " كلهم ", " كل واحد ", " زي بعض ", " نفس السن ", " both "];

    /// <summary>v1.71: the words that name the wife or the husband before an age (normalized: ه for ة, ي for ى, ا for أ).</summary>
    private static readonly HashSet<string> WifeWords = new(StringComparer.Ordinal)
        { "الزوجه", "زوجه", "زوجتي", "مراتي", "مراته", "العروسه", "عروسه", "العروس", "هي", "البنت", "الست", "خطيبتي" };

    private static readonly HashSet<string> HusbandWords = new(StringComparer.Ordinal)
        { "الزوج", "زوج", "زوجها", "جوزها", "انا", "العريس", "عريس", "هو", "الراجل", "خطيبها" };

    private static char? RoleOf(string raw)
    {
        var w = Courtesy.Normalize(raw).Trim();
        foreach (var form in w.Length > 2 && w[0] is 'و' or 'ب' or 'ل' ? new[] { w, w[1..] } : [w])
        {
            if (WifeWords.Contains(form))
            {
                return 'w';
            }

            if (HusbandWords.Contains(form))
            {
                return 'h';
            }
        }

        return null;
    }
}

public interface IUrfStore
{
    /// <summary>The client's Urfi cases still running (collecting, documents_pending, booked), the newest first (one per kind).</summary>
    Task<IReadOnlyList<UrfCase>> OpenAsync(Guid personId, CancellationToken ct);

    Task SaveAsync(UrfCase c, Guid? intakeId, CancellationToken ct);

    /// <summary>v1.20 (B-11.1): the client's newest age refusal since <paramref name="since"/>, with the closing texts sent after it.</summary>
    Task<AgeRefusal?> AgeRefusalAsync(Guid personId, DateTimeOffset since, CancellationToken ct);

    /// <summary>hazem.add_urf_file: a document for the case waiting for them.</summary>
    Task<AddFileOutcome> AddFileAsync(Guid personId, StoredFile file, CancellationToken ct);
}

/// <summary>Where the flow keeps the Urfi case and hands it the client's documents.</summary>
public sealed class UrfDesk(IUrfStore store)
{
    public Task<IReadOnlyList<UrfCase>> OpenAsync(Guid personId, CancellationToken ct) => store.OpenAsync(personId, ct);

    public Task SaveAsync(UrfCase c, Guid? intakeId, CancellationToken ct) => store.SaveAsync(c, intakeId, ct);

    public Task<AgeRefusal?> AgeRefusalAsync(Guid personId, DateTimeOffset now, CancellationToken ct) =>
        store.AgeRefusalAsync(personId, now - UrfRules.AgeRefusalWindow, ct);

    /// <summary>Every stored file of the turn. First when any of them was the case's first.</summary>
    public async Task<AddFileOutcome> AddFilesAsync(Guid personId, IEnumerable<StoredFile> files, CancellationToken ct)
    {
        var best = new AddFileOutcome(AddFileStatus.None, null, null);
        foreach (var f in files)
        {
            best = ServiceDesk.Better(best, await store.AddFileAsync(personId, f, ct));
        }

        return best;
    }
}
