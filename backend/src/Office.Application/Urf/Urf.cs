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
    string? PolicyVersion = null, string? GuardianOut = null)
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

    /// <summary>
    /// v1.87 (Ahmed Amin's chat: «اخو العروسه مش هيجي» brought the guardian question back with «الأخ» in it): the guardians he said
    /// will not come or do not agree, kept with the case, so the guardian question is asked without them. Named again as the
    /// guardian («لا خلاص اخوها جاي»), one leaves this list.
    /// </summary>
    public IReadOnlyList<string> Out => GuardianOut is { Length: > 0 } g ? g.Split(',') : [];

    /// <summary>v1.87: the list as it is kept (comma-separated, no repeats; null for none).</summary>
    public static string? OutOf(IEnumerable<string> guardians)
    {
        var kept = guardians.Where(g => g is "father" or "brother" or "uncle").Distinct().ToList();
        return kept.Count == 0 ? null : string.Join(',', kept);
    }

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

    /// <summary>v1.82: «لا» under «ينفع الأب؟»: that one does not come; nothing in the case changes.</summary>
    public const string GuardianNo = "no";

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
/// <remarks>v1.72: <see cref="Case"/> is the refused case itself, so what he says after it can change the answer it was refused for.</remarks>
public sealed record AgeRefusal(DateTimeOffset At, int ClosingReplies, bool MovedOn = false, string State = UrfCase.RejectedAge,
    UrfCase? Case = null)
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
            (UrfStep.Guardian, "father" or "brother" or "uncle") => Advance(c with { Guardian = a.Value, GuardianOut = UrfCase.OutOf(c.Out.Except([a.Value])) }),
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
    /// documents, a refund are the office's decision): the office is told. v1.74: «no guardian» and the ages too.
    /// </summary>
    public static bool ChangesBooked(UrfCase c, string? wifePast, string? guardian, string? witnesses, int? husbandAge = null, int? wifeAge = null,
        IReadOnlyCollection<string>? withdrawn = null)
    {
        var locked = c.State is UrfCase.Booked or UrfCase.Approved || c.State == UrfCase.DocumentsPending && c.AppointmentId is not null;
        static bool Differs(string? now, string? said, params string[] allowed) => said is not null && allowed.Contains(said) && said != now;
        bool Took(string field, object? now) => withdrawn?.Contains(field) == true && now is not null; // v1.84: an answer taken back
        return locked && (Differs(c.Witnesses, witnesses, "office", "own") || Took(UrfStep.Witnesses, c.Witnesses)
            || c.IsMarriage && (Differs(c.WifePast, wifePast, "first", "divorced", "khul", "widow") || Took(UrfStep.WifePast, c.WifePast)
                || Differs(c.Guardian, guardian, "father", "brother", "uncle", "none") || Took(UrfStep.Guardian, c.Guardian)
                || Ages(husbandAge, wifeAge) is { } said && said != (c.HusbandAge, c.WifeAge) || Took(UrfStep.Ages, c.HusbandAge)));
    }

    /// <summary>TASK 20 (v1.84): the same check for what the AI read this turn.</summary>
    public static bool ChangesBooked(UrfCase c, UrfFacts f) =>
        ChangesBooked(c, f.WifePast, f.Guardian, f.Witnesses, f.HusbandAge, f.WifeAge,
            c.Guardian is { } g && f.NotComing.Contains(g) ? [.. f.Withdrawn, UrfStep.Guardian] : f.Withdrawn); // v1.87: the guardian will not come

    /// <summary>v1.74: what the step means for the reply: the next question, the price and times, or the refusal.</summary>
    public static UrfNext NextOf(UrfCase c) => c.State switch
    {
        UrfCase.Collecting => UrfNext.Ask,
        UrfCase.RejectedGuardian => UrfNext.RejectGuardian,
        UrfCase.RejectedAge => UrfNext.RejectAge,
        _ => UrfNext.Book,
    };

    /// <summary>
    /// v1.75: what he corrected (an answer given before that is now different), in the words said back before the next question:
    /// «السن: الزوج 26 سنة والزوجة 25 سنة»، «إنها اتطلقت أو اختلعت قبل كده»… joined with «، و». Null when nothing given was changed
    /// (an answer filled for the first time is no correction).
    /// </summary>
    public static string? Corrected(UrfCase before, UrfCase after)
    {
        var said = new List<string>();
        if (before.HusbandAge is not null && (before.HusbandAge, before.WifeAge) != (after.HusbandAge, after.WifeAge))
        {
            said.Add($"السن: الزوج {after.HusbandAge} سنة والزوجة {after.WifeAge} سنة");
        }

        if (before.WifePast is not null && before.WifePast != after.WifePast && after.WifePast is { } past)
        {
            said.Add(past switch
            {
                "first" => "إنها أول جوازة ليها",
                "widow" => "إنها أرملة",
                _ => "إنها اتطلقت أو اختلعت قبل كده",
            });
        }

        if (before.Guardian is not null && before.Guardian != after.Guardian && UrfCase.GuardianName(after.Guardian) is { Length: > 0 } guardian)
        {
            said.Add($"الولي يبقى {guardian}");
        }

        if (before.Witnesses is not null && before.Witnesses != after.Witnesses && after.Witnesses is { } witnesses)
        {
            said.Add(witnesses == "office" ? "المكتب هيوفّر الشهود" : "الشهود هتجيبهم حضرتك");
        }

        return said.Count == 0 ? null : string.Join("، و", said);
    }

    private static (int, int)? Ages(int? husband, int? wife) =>
        husband is >= 1 and <= 120 && wife is >= 1 and <= 120 ? (husband.Value, wife.Value) : null;

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

    private static (UrfCase Case, UrfNext Next) Advance(UrfCase c)
    {
        var settled = Settle(c);
        return (settled, NextOf(settled));
    }

    /// <summary>
    /// v1.74: the answers as they are now, through B-11's rules in its order: once the ages are known, under 18 refuses, then «no
    /// guardian» for a first marriage refuses; otherwise the next question, or the price and times when nothing is left.
    /// </summary>
    private static UrfCase Settle(UrfCase c)
    {
        if (c.HusbandAge is not null && (c.HusbandAge < MinAge || c.WifeAge < MinAge))
        {
            return c with { State = UrfCase.RejectedAge, Step = null, PolicyVersion = PolicyVersion };
        }

        if (c.HusbandAge is not null && c.NeedsGuardian && c.Guardian == "none")
        {
            return c with { State = UrfCase.RejectedGuardian, Step = null, PolicyVersion = PolicyVersion };
        }

        return NextStep(c) is { } step
            ? c with { State = UrfCase.Collecting, Step = step }
            : c with { State = UrfCase.DocumentsPending, Step = null };
    }

    /// <summary>
    /// v1.60 (the owner: «قال انها مطلقه ليه البوت ساله بعد كدا؟» and «عايز الشهود من المكتب» got the price of his own
    /// witnesses): what the client said in his own words, as the AI read it from the chat. v1.74 (Ahmed Amin's chat: «لا قصدي
    /// الزوجه 25» and «اقصد لا مفيش حد من اهل الزوجه» were not taken, and the case kept «the father»): what he says is the
    /// answer, filled or CORRECTED, while the questions are collected and while the price and times wait for a pick; then the
    /// same rules run again on the answers as they are now (under 18, no guardian for a first marriage, the next question).
    /// A case with a time held, or closed, is the office's. Values outside the lists are ignored.
    /// v1.82 / TASK 20 (v1.84): an answer he TAKES BACK («اخوها مش هيجي»، «الشهود مش هييجوا»), with nothing said in its place, is
    /// unknown again, so its question comes back; only «none» refuses. <paramref name="withdrawn"/> holds those fields.
    /// </summary>
    public static UrfCase ApplyFacts(UrfCase c, string? wifePast, string? guardian, string? witnesses, int? husbandAge = null, int? wifeAge = null,
        IReadOnlyCollection<string>? withdrawn = null, IReadOnlyCollection<string>? notComing = null)
    {
        static string? Only(string? v, params string[] allowed) => v is not null && allowed.Contains(v) ? v : null;
        var past = c.IsMarriage ? Only(wifePast, "first", "divorced", "khul", "widow") : null;
        var guard = c.IsMarriage ? Only(guardian, "father", "brother", "uncle", "none") : null;
        var wit = Only(witnesses, "office", "own");
        var ages = c.IsMarriage ? Ages(husbandAge, wifeAge) : null;
        // v1.87: who will not come is kept (one named as the guardian leaves the list); the guardian he had is then taken back,
        // and when none of the three is left it is «nobody of her family» (B-11), as if he said it
        var outNow = c.IsMarriage ? c.Out.Concat(notComing ?? []).Where(g => g != guard).Distinct().ToList() : [];
        bool Took(string field) => withdrawn?.Contains(field) == true
                                   || field == UrfStep.Guardian && c.Guardian is { } had && notComing?.Contains(had) == true;
        if (c.State == UrfCase.Collecting || c.State == UrfCase.DocumentsPending && c.AppointmentId is null)
        {
            var kept = guard ?? (Took(UrfStep.Guardian) ? null : c.Guardian);
            var now = c with
            {
                WifePast = past ?? (Took(UrfStep.WifePast) ? null : c.WifePast),
                Guardian = kept is null && outNow.Count >= 3 ? "none" : kept,
                GuardianOut = UrfCase.OutOf(outNow),
                Witnesses = wit ?? (Took(UrfStep.Witnesses) ? null : c.Witnesses),
                HusbandAge = ages?.Item1 ?? (Took(UrfStep.Ages) ? null : c.HusbandAge),
                WifeAge = ages?.Item2 ?? (Took(UrfStep.Ages) ? null : c.WifeAge),
            };
            return now == c ? c : Settle(now);
        }

        // v1.72 (Ahmed Amin's chat: «طب هجيب ابوها» after «مفيش حد منهم» got the closing text, then silence): the refusal for no
        // guardian is the answer «none» like any other answer, so what he says after it changes it as it changes the others: a
        // guardian named, or a past that needs none. The case goes on from where it stopped, as a new case (a refused case is never
        // written again); its other answers are kept. The age refusal has nothing to change: it stays.
        if (c.State == UrfCase.RejectedGuardian && (guard is not null and not "none" || past is not null and not "first"))
        {
            var again = c with
            {
                Id = Guid.NewGuid(), State = UrfCase.Collecting, Step = null, PolicyVersion = null,
                WifePast = past ?? c.WifePast, Guardian = guard is "none" ? null : guard, Witnesses = c.Witnesses ?? wit,
                GuardianOut = UrfCase.OutOf(outNow),
            };
            return NextStep(again) is { } next
                ? again with { Step = next }
                : again with { State = UrfCase.DocumentsPending };
        }

        return c;
    }

    /// <summary>TASK 20 (v1.84): <see cref="ApplyFacts(UrfCase, string?, string?, string?, int?, int?, IReadOnlyCollection{string}?)"/> for what the AI read.</summary>
    public static UrfCase ApplyFacts(UrfCase c, UrfFacts f) =>
        ApplyFacts(c, f.WifePast, f.Guardian, f.Witnesses, f.HusbandAge, f.WifeAge, f.Withdrawn, f.NotComing);

    /// <summary>
    /// TASK 20 (v1.84, the owner: «عايز حلول عامه»): a question about one answer («ينفع ابوها؟»، «ينفع أجيب الشهود؟»، «لو مطلقة ينفع؟»،
    /// «لو هي 17 ينفع؟») is answered by the SAME B-11 rules that decide the case: the asked value is tried on a copy of the case
    /// (<see cref="Settle"/>), and the case itself never changes. Refused by a rule → that rule's «cannot»; goes on → «can» (the
    /// guardian, a person who must come, then asks back whether he comes). A relative outside the allowed ones is «cannot».
    /// Null: nothing to answer here (a divorce question, a guardian a widow does not need, the same answer it already has).
    /// </summary>
    public static UrfAskAnswer? Try(UrfCase c, UrfSaid asked)
    {
        if (!c.IsMarriage || asked.Kind != UrfKind.Asked)
        {
            return null;
        }

        // the refused-for-no-guardian case is tried as the case it would be again (v1.72)
        var open = c.State == UrfCase.RejectedGuardian ? c with { State = UrfCase.Collecting, Guardian = null } : c;
        switch (asked.Field)
        {
            case UrfStep.Guardian when c.WifePast is not (null or "first"):
                return null; // v1.68: a guardian is needed for a first marriage only
            case UrfStep.Guardian when asked.Value is "father" or "brother" or "uncle":
                return c.Guardian == asked.Value && c.State != UrfCase.RejectedGuardian
                    ? null
                    : new UrfAskAnswer(UrfAskAnswer.GuardianCan, asked.Value);
            case UrfStep.Guardian when asked.Value == "none":
                return new UrfAskAnswer(UrfAskAnswer.GuardianNone, null);
            case UrfStep.Guardian when asked.Value == "other":
                return new UrfAskAnswer(UrfAskAnswer.GuardianCannot, asked.Word);
            case UrfStep.WifePast when asked.Value is "first" or "divorced" or "khul" or "widow":
                return Of(Settle(open with { WifePast = asked.Value }), asked);
            // v1.90 (Ahmed Amin's chat: «لازم يكون في شهود؟ ولا ممكن نعمل العقد بينا إحنا الاتنين بس؟»): the contract always has two
            // witnesses, the office's or his: the office's rule, said as an answer (never a legal opinion), nothing changes
            case UrfStep.Witnesses when asked.Value == "none":
                return new UrfAskAnswer(UrfAskAnswer.WitnessesNeeded, null);
            case UrfStep.Witnesses when asked.Value is "own" or "office":
                return Of(Settle(open with { Witnesses = asked.Value }), asked);
            case UrfStep.Ages when asked.HusbandAge is not null || asked.WifeAge is not null:
                var tried = open with { HusbandAge = asked.HusbandAge ?? open.HusbandAge ?? MinAge, WifeAge = asked.WifeAge ?? open.WifeAge ?? MinAge };
                return Of(Settle(tried), asked);
            default:
                return null;
        }

        static UrfAskAnswer Of(UrfCase tried, UrfSaid asked) => tried.State switch
        {
            UrfCase.RejectedAge => new UrfAskAnswer(UrfAskAnswer.AgeCannot, null),
            UrfCase.RejectedGuardian => new UrfAskAnswer(UrfAskAnswer.GuardianNone, null),
            _ => new UrfAskAnswer(UrfAskAnswer.OptionCan, OptionWords(asked)),
        };
    }

    /// <summary>v1.84: {{option}} of «أيوه، {{option}} ينفع ✅», in the field's own words (never his).</summary>
    public static string OptionWords(UrfSaid asked) => (asked.Field, asked.Value) switch
    {
        (UrfStep.WifePast, "first") => "إن دي تكون أول جوازة للزوجة",
        (UrfStep.WifePast, "divorced" or "khul") => "إن الزوجة تكون مطلقة أو مختلعة",
        (UrfStep.WifePast, "widow") => "إن الزوجة تكون أرملة",
        (UrfStep.Witnesses, "own") => "إن حضرتك تجيب الشهود",
        (UrfStep.Witnesses, "office") => "إن المكتب يوفّر الشهود",
        _ => "السن ده",
    };

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

    /// <summary>
    /// v1.90 (Ahmed Amin's chat: «77و90» and «طب هو لازم يكون في شهود؟» in one turn: the code took the ages and threw the question
    /// away): the message is the two ages and nothing else, so the code may read it without the AI. Nothing may be left once the
    /// numbers, the words naming the husband or the wife, the words for «age / years» and the «و» between them are taken out. Any
    /// other word goes to the AI, which reads the ages in it too (a word missing here only costs one AI call, never an answer).
    /// </summary>
    public static bool IsOnly(string? text)
    {
        if (Parse(text) is null)
        {
            return false;
        }

        var normalized = $" {Courtesy.Normalize(text!)} ";
        foreach (var both in BothWords)
        {
            normalized = normalized.Replace(both, " ", StringComparison.Ordinal);
        }

        var words = new System.Text.StringBuilder();
        foreach (var ch in normalized)
        {
            words.Append(char.IsLetter(ch) ? ch : ' ');
        }

        foreach (var raw in words.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = raw.Length > 1 && raw[0] == 'و' ? raw[1..] : raw;
            if (raw != "و" && RoleOf(raw) is null && !AgeWords.Contains(w) && !AgeWords.Contains(raw))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>v1.90: the words for «age / years» around the two numbers (normalized).</summary>
    private static readonly HashSet<string> AgeWords = new(StringComparer.Ordinal)
        { "سن", "السن", "سنه", "سنين", "سنوات", "عام", "عمر", "عمره", "عمرها", "عمري", "عنده", "عندها", "years", "year", "and" };

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

/// <summary>TASK 20 (v1.84): how he speaks of one answer of his Urfi case (classify-v30 <c>case_said</c>).</summary>
public static class UrfKind
{
    /// <summary>He states it («ابوها موجود»، «هنجيب الشهود»).</summary>
    public const string Said = "said";

    /// <summary>He asks whether it is allowed («ينفع ابوها؟»، «لو مطلقة ينفع؟»).</summary>
    public const string Asked = "asked";

    /// <summary>He takes an answer back, saying nothing in its place («اخوها مش هيجي»).</summary>
    public const string Withdrawn = "withdrawn";

    /// <summary>v1.87: a guardian he says will not come or does not agree («اخو العروسه مش هيجي»), kept so he is not offered again.</summary>
    public const string NotComing = "not_coming";
}

/// <summary>
/// TASK 20 (v1.84): one thing the client says about his Urfi case in this turn, as the AI read it. <see cref="Field"/> is a
/// <see cref="UrfStep"/>; <see cref="Value"/> one of that field's values («other»: a relative outside the allowed ones); the ages
/// come as two numbers; <see cref="Word"/> is his own word for an «other» relative («خالها»), used only when it is in his message.
/// </summary>
/// v1.87: <see cref="Quote"/> is his own words that say it, copied by the AI; the item is kept only when they are in his messages
/// (<see cref="UrfFacts.Quoted"/>), so the AI never changes an answer he did not speak of. Null only from callers without it.
public sealed record UrfSaid(string Field, string Kind, string? Value = null, int? HusbandAge = null, int? WifeAge = null, string? Word = null,
    string? Quote = null);

/// <summary>
/// TASK 20 (v1.84): what the turn's items say, the LATEST per field (2.1 of TASK 20): its stated answers, the fields he took
/// back, and the question he asked when that is the latest he said of its field.
/// </summary>
public sealed record UrfFacts(
    string? WifePast, string? Guardian, string? Witnesses, int? HusbandAge, int? WifeAge, IReadOnlyCollection<string> Withdrawn, UrfSaid? Asked,
    IReadOnlyCollection<string>? Out = null, IReadOnlyList<UrfSaid>? AskedAll = null)
{
    public static readonly UrfFacts None = new(null, null, null, null, null, [], null);

    /// <summary>v1.87: the guardians he said will not come.</summary>
    public IReadOnlyCollection<string> NotComing => Out ?? [];

    /// <summary>v1.87: every question he asked about his case, in his order (none is dropped by what he says around it).</summary>
    public IReadOnlyList<UrfSaid> Questions => AskedAll ?? (Asked is { } a ? [a] : []);

    public bool Any => WifePast is not null || Guardian is not null || Witnesses is not null || HusbandAge is not null || WifeAge is not null
                       || Withdrawn.Count > 0 || NotComing.Count > 0;

    /// <summary>
    /// v1.87 (the owner: the AI may change an answer he did not speak of): only the items whose words are in his own messages (this one
    /// and the ones before it in the chat, for «منا قولتلك»), compared without spelling marks. An item with no words (callers that do
    /// not carry them) is kept as before.
    /// </summary>
    public static IReadOnlyList<UrfSaid>? Quoted(IReadOnlyList<UrfSaid>? items, IReadOnlyList<string>? his)
    {
        if (items is null || his is null)
        {
            return items;
        }

        var texts = his.Select(Squeeze).ToList();
        return items.Where(i => i.Quote is null || Squeeze(i.Quote) is { Length: > 1 } q && texts.Any(t => t.Contains(q, StringComparison.Ordinal))).ToList();
    }

    private static string Squeeze(string text) => string.Join(' ', Office.Application.Understanding.Courtesy.Normalize(
        NumberWords.ToDigits(text)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>v1.84: hazem.urf_case_said off: v1.81's fields one by one (only stated answers; nothing asked or taken back).</summary>
    public static UrfFacts Fields(string? wifePast, string? guardian, string? witnesses, int? husbandAge, int? wifeAge) =>
        new(wifePast, guardian, witnesses, husbandAge, wifeAge, [], null);

    public static UrfFacts Of(IReadOnlyList<UrfSaid>? items)
    {
        if (items is null || items.Count == 0)
        {
            return None;
        }

        // v1.87 (Ahmed Amin's chat: «ينفع اخلي الشهود من المكتب الشهود عندي مش هيجوا» lost the question to the answer taken back
        // after it): the AI gives what this message CHANGES, per field (a later one of a field wins); a question is answered
        // whatever is said around it, unless he then SAYS that field's answer («ينفع ابوها؟… خلاص ابوها جاي»)
        var latest = new Dictionary<string, UrfSaid>();
        foreach (var i in items.Where(i => i.Kind is UrfKind.Said or UrfKind.Withdrawn))
        {
            latest[i.Field] = i;
        }

        string? Said(string field) => latest.TryGetValue(field, out var i) && i.Kind == UrfKind.Said ? i.Value : null;
        var ages = latest.TryGetValue(UrfStep.Ages, out var a) && a.Kind == UrfKind.Said ? a : null;
        var asked = items.Select((i, n) => (i, n))
            .Where(x => x.i.Kind == UrfKind.Asked && !items.Skip(x.n + 1).Any(s => s.Kind == UrfKind.Said && s.Field == x.i.Field))
            .Select(x => x.i).ToList();
        var notComing = items.Where(i => i.Kind == UrfKind.NotComing && i.Field == UrfStep.Guardian && i.Value is "father" or "brother" or "uncle")
            .Select(i => i.Value!).Distinct().ToList();
        return new UrfFacts(Said(UrfStep.WifePast), Said(UrfStep.Guardian), Said(UrfStep.Witnesses), ages?.HusbandAge, ages?.WifeAge,
            latest.Values.Where(i => i.Kind == UrfKind.Withdrawn).Select(i => i.Field).ToList(), asked.LastOrDefault(), notComing, asked);
    }
}

/// <summary>TASK 20 (v1.84): the answer to a question about one Urfi answer (<see cref="UrfRules.Try"/>).</summary>
public sealed record UrfAskAnswer(string What, string? Value)
{
    /// <summary>«أيوه، الأب ينفع يبقى الولي. هو هيحضر معاكم وموافق؟» (Value: the guardian).</summary>
    public const string GuardianCan = "guardian_can";

    /// <summary>«للأسف القريب ده مينفعش يبقى الولي…», then the guardian question.</summary>
    public const string GuardianCannot = "guardian_cannot";

    /// <summary>«لو مفيش حد ينفع؟»: the guardian policy, nothing closes.</summary>
    public const string GuardianNone = "guardian_none";

    /// <summary>«أيوه، {{option}} ينفع ✅» (Value: the option's words), then the question waiting.</summary>
    public const string OptionCan = "option_can";

    /// <summary>«للأسف لأ، سياسة المكتب إن الزوج والزوجة يكون سنهم 18 سنة أو أكتر.», then the question waiting.</summary>
    public const string AgeCannot = "age_cannot";

    /// <summary>v1.90: «لازم يكون في شهود؟ ولا العقد بينا بس؟»: «أيوه، العقد عندنا لازم يكون فيه شاهدين…», then the question waiting.</summary>
    public const string WitnessesNeeded = "witnesses_needed";
}

/// <summary>
/// TASK 20 (v1.84): the switch hazem.urf_case_said. Off (the default): the Urfi case exactly as v1.81 (classify-v27 and its fields,
/// the AI's 15 seconds). On: v1.82–v1.84 (classify-v30's one list, the questions answered by the rules, 30 seconds).
/// </summary>
public static class UrfCaseSaid
{
    public const string SwitchKey = "hazem.urf_case_said";
}
