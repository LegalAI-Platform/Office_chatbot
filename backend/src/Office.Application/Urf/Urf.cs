using System.Globalization;
using Office.Application.Booking;
using Office.Application.Services;
using Office.Application.Understanding;

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

    public static UrfCase Start(Guid personId, string kind) =>
        new(Guid.NewGuid(), personId, kind, Collecting, kind == Marriage ? UrfStep.Ages : UrfStep.HasCopy);

    public bool IsMarriage => Kind == Marriage;

    /// <summary>The booking it leads to: the contract or the divorce, with the office's witnesses (5,000) or theirs (4,000).</summary>
    public BookingService Service => BookingService.Urf(IsMarriage, Witnesses == "office");

    /// <summary>B-11.5: what to send, from the answers.</summary>
    public string Documents()
    {
        var items = IsMarriage
            ? new List<string> { "بطاقة الزوج", "بطاقة الزوجة", $"بطاقة الولي ({GuardianName(Guardian)})" }
            : ["نسخة العقد العرفي", "بطاقة الزوج", "بطاقة الزوجة"];
        if (WifePast == "divorced")
        {
            items.Add("مستند الطلاق");
        }
        else if (WifePast == "khul")
        {
            items.Add("حكم الخلع");
        }

        if (Witnesses == "own")
        {
            items.Add("بطاقتين الشاهدين");
        }

        return string.Join("، و", items);
    }

    /// <summary>B-11.7: who comes to the office.</summary>
    public string Attendees() => AttendeesFor(Kind, Witnesses);

    /// <summary>B-11.7 for a case read elsewhere (TASK 12: the reminder of its appointment).</summary>
    public static string AttendeesFor(string kind, string? witnesses) =>
        (kind == Marriage ? "الزوج والزوجة والولي" : "الطرفين") + (witnesses == "own" ? " والشاهدين" : "");

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
public sealed record AgeRefusal(DateTimeOffset At, int ClosingReplies, bool MovedOn = false);

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
            (UrfStep.WifePast, "first" or "divorced" or "khul") => (c with { WifePast = a.Value, Step = UrfStep.Guardian }, UrfNext.Ask),
            (UrfStep.Guardian, "none") => (c with { Guardian = "none", State = UrfCase.RejectedGuardian, Step = null, PolicyVersion = PolicyVersion },
                UrfNext.RejectGuardian),
            (UrfStep.Guardian, "father" or "brother" or "uncle") => (c with { Guardian = a.Value, Step = UrfStep.Witnesses }, UrfNext.Ask),
            (UrfStep.Witnesses, "office" or "own") => (c with { Witnesses = a.Value, State = UrfCase.DocumentsPending, Step = null }, UrfNext.Book),
            (UrfStep.HasCopy, "yes") => (c with { HasCopy = true, Step = UrfStep.Agreed }, UrfNext.Ask),
            (UrfStep.HasCopy, "no") => (Redirect(c with { HasCopy = false }), UrfNext.Redirect),
            (UrfStep.Agreed, "yes") => (c with { Agreed = true, Step = UrfStep.Children }, UrfNext.Ask),
            (UrfStep.Agreed, "no") => (Redirect(c with { Agreed = false }), UrfNext.Redirect),
            (UrfStep.Children, "no") => (c with { Children = false, Step = UrfStep.Witnesses }, UrfNext.Ask),
            (UrfStep.Children, "yes") => (Redirect(c with { Children = true }), UrfNext.Redirect),
            _ => (c, UrfNext.Ask),
        };
    }

    /// <summary>B-11.1: the typed ages. Under 18 on either side closes the case without a price or a time.</summary>
    public static (UrfCase Case, UrfNext Next) Ages(UrfCase c, string? text)
    {
        if (c.State != UrfCase.Collecting || c.Step != UrfStep.Ages)
        {
            return (c, UrfNext.Ask);
        }

        if (UrfAges.Parse(text) is not { } ages)
        {
            var tried = c with { AgeTries = c.AgeTries + 1 };
            return (tried, tried.AgeTries >= 2 ? UrfNext.Help : UrfNext.AskAgesAgain);
        }

        var (husband, wife) = ages;
        var answered = c with { HusbandAge = husband, WifeAge = wife };
        return husband < MinAge || wife < MinAge
            ? (answered with { State = UrfCase.RejectedAge, Step = null, PolicyVersion = PolicyVersion }, UrfNext.RejectAge)
            : (answered with { Step = UrfStep.WifePast }, UrfNext.Ask);
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
        var digits = new System.Text.StringBuilder();
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
                digits.Clear();
            }
        }

        return numbers is [var h, var w] && h is >= 1 and <= 120 && w is >= 1 and <= 120 ? (h, w) : null;
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
