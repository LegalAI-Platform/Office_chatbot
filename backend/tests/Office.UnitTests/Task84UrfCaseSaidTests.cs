using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.84 (TASK 20, the owner: «عايز حلول عامه»): what the client says about his Urfi case is one list (said / asked /
/// withdrawn), the latest per field. A question about one answer is answered by the case's own rules on a copy (never the office,
/// never a change); an answer taken back brings its question back; several things in one turn are all taken.
/// </summary>
public sealed partial class BookingFlowTests
{
    private static UrfSaid Asked(string field, string? value = null, int? husband = null, int? wife = null, string? word = null) =>
        new(field, UrfKind.Asked, value, husband, wife, word);

    private static UrfSaid SaidItem(string field, string value) => new(field, UrfKind.Said, value);

    private static UrfSaid Took(string field, string value) => new(field, UrfKind.Withdrawn, value);

    /// <summary>v1.84: understanding v2 and the switch hazem.urf_case_said on (v1.82–v1.84).</summary>
    private void CaseSaidOn()
    {
        UrfFactsOn();
        _settings[UrfCaseSaid.SwitchKey] = "true";
    }

    private void CaseAnswersOn()
    {
        CaseSaidOn();
        GuardianCanOn();
    }

    [Fact]
    public async Task With_the_switch_off_a_question_is_never_answered_from_the_list_as_v1_81()
    {
        UrfFactsOn(); // understanding v2 on, hazem.urf_case_said off
        GuardianCanOn();
        await ToTheWifePast();
        _script["لو هي مطلقة ينفع؟"] = new("other", null, "unknown", null, 0.8, CaseSaid: [Asked(UrfStep.WifePast, "divorced")]);
        Assert.DoesNotContain(TextKeys.UrfOptionCan, Keys(await Say("لو هي مطلقة ينفع؟")));
        Assert.Null(TheCase.WifePast);
    }

    [Fact]
    public async Task With_the_switch_off_the_fields_one_by_one_are_read_and_the_list_is_not()
    {
        UrfFactsOn();
        await ToTheWifePast();
        _script["هي مطلقة"] = new("other", null, "unknown", null, 0.8, UrfWifePast: "divorced",
            CaseSaid: [SaidItem(UrfStep.WifePast, "widow")]);
        await Say("هي مطلقة");
        Assert.Equal("divorced", TheCase.WifePast); // v1.81's field, never the list
    }

    [Fact]
    public async Task With_the_switch_off_the_refusal_opened_again_is_not_said_back_as_v1_81()
    {
        UrfFactsOn();
        CorrectedOn();
        await ToTheGuardian();
        _texts[TextKeys.UrfGuardianClosed] = new ApprovedText(TextKeys.UrfGuardianClosed, 1, "T:guardian closed", 300, true);
        await Answer(UrfStep.Guardian, "none");
        _script["ابوها موجود"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9, UrfGuardian: "father");
        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(await Say("ابوها موجود")));
    }

    [Fact]
    public async Task Asking_whether_a_past_is_allowed_is_answered_and_the_question_waits_again()
    {
        CaseAnswersOn();
        await ToTheWifePast();
        _script["لو هي مطلقة ينفع؟"] = new("office_info", "urf_marriage", "unknown", null, 0.8, InfoTopic: "service_detail",
            CaseSaid: [Asked(UrfStep.WifePast, "divorced")]);
        var d = await Say("لو هي مطلقة ينفع؟");
        Assert.Equal([TextKeys.UrfOptionCan, TextKeys.UrfAskWifePast], Keys(d)); // never «هوصّله للمكتب»
        Assert.Contains("إن الزوجة تكون مطلقة أو مختلعة", d.Actions[0].Text);
        Assert.Null(TheCase.WifePast); // a question changes nothing
        Assert.DoesNotContain(PendingTask.ServiceQuestion, TaskKinds(d));
    }

    [Fact]
    public async Task Asking_about_the_witnesses_is_answered_with_their_question_and_nothing_is_taken()
    {
        CaseAnswersOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "father");
        _script["ينفع أجيب الشهود من عندي؟"] = new("other", null, "unknown", null, 0.8, CaseSaid: [Asked(UrfStep.Witnesses, "own")]);
        var d = await Say("ينفع أجيب الشهود من عندي؟");
        Assert.Equal([TextKeys.UrfOptionCan, TextKeys.UrfAskWitnesses], Keys(d));
        Assert.Contains("إن حضرتك تجيب الشهود", d.Actions[0].Text);
        Assert.Null(TheCase.Witnesses);
    }

    [Fact]
    public async Task An_asked_age_under_18_is_the_policy_and_nothing_closes()
    {
        CaseAnswersOn();
        await ToTheOffer();
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say("عايز أعمل جواز عرفي")));
        _script["لو هي 17 سنة ينفع؟"] = new("other", null, "unknown", null, 0.8, CaseSaid: [Asked(UrfStep.Ages, wife: 17)]);
        Assert.Equal([TextKeys.UrfAgeCannot, TextKeys.UrfAskAges], Keys(await Say("لو هي 17 سنة ينفع؟")));
        Assert.Equal((UrfCase.Collecting, UrfStep.Ages), (TheCase.State, TheCase.Step));
    }

    [Theory]
    [InlineData("ينفع خالها؟", "خالها", "للأسف خالها مينفعش")]
    [InlineData("طب اللي من ناحية امها ينفع؟", "الخال", "للأسف القريب ده مينفعش")] // his word is not in his message: never the AI's
    public async Task A_relative_outside_the_three_is_cannot_and_the_guardian_question_follows(string words, string word, string said)
    {
        CaseAnswersOn();
        await ToTheGuardian();
        _script[words] = new("other", null, "unknown", null, 0.8, CaseSaid: [Asked(UrfStep.Guardian, "other", word: word)]);
        var d = await Say(words);
        Assert.Equal([TextKeys.UrfGuardianCannot, TextKeys.UrfAskGuardian], Keys(d));
        Assert.StartsWith(said, d.Actions[0].Text);
        Assert.Equal(UrfCase.Collecting, TheCase.State);
    }

    [Fact]
    public async Task Asking_whether_nobody_is_allowed_gets_the_policy_and_the_case_stays()
    {
        CaseAnswersOn();
        await ToTheGuardian();
        _script["لو مفيش حد من أهلها ينفع؟"] = new("other", null, "unknown", null, 0.8, CaseSaid: [Asked(UrfStep.Guardian, "none")]);
        Assert.Equal([TextKeys.UrfGuardianClosed], Keys(await Say("لو مفيش حد من أهلها ينفع؟")));
        Assert.Equal((UrfCase.Collecting, UrfStep.Guardian), (TheCase.State, TheCase.Step));
    }

    [Fact]
    public async Task The_named_one_taken_back_and_another_asked_in_one_turn_asks_back_about_the_other()
    {
        CaseAnswersOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Answer(UrfStep.Witnesses, "own")));
        const string words = "بقولك ايه اخوها مش هيجي لسه مبلغني ب كده\nابوها موجود ينفع؟";
        _script[words] = new("office_info", "urf_marriage", "unknown", null, 0.75, InfoTopic: "service_detail",
            CaseSaid: [Took(UrfStep.Guardian, "brother"), Asked(UrfStep.Guardian, "father")]);
        var d = await Say(words);
        Assert.Equal([TextKeys.UrfGuardianCan], Keys(d));
        Assert.Contains("الأب ينفع يبقى الولي", d.Actions[0].Text);
        Assert.DoesNotContain(PendingTask.ServiceQuestion, TaskKinds(d));

        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Answer(UrfStep.Guardian, "father"))); // the brother was already taken back: no correction
        Assert.Equal("father", TheCase.Guardian);
    }

    [Fact]
    public async Task What_he_says_with_a_question_is_taken_and_the_question_answered()
    {
        CaseAnswersOn();
        await ToTheWifePast();
        _script["الشهود هنجيبهم ولو مطلقة ينفع؟"] = new("other", null, "unknown", null, 0.8,
            CaseSaid: [SaidItem(UrfStep.Witnesses, "own"), Asked(UrfStep.WifePast, "divorced")]);
        Assert.Equal([TextKeys.UrfOptionCan, TextKeys.UrfAskWifePast], Keys(await Say("الشهود هنجيبهم ولو مطلقة ينفع؟")));
        Assert.Equal(("own", (string?)null), (TheCase.Witnesses, TheCase.WifePast));
    }

    [Fact]
    public async Task Witnesses_taken_back_while_the_times_wait_bring_their_question_back()
    {
        CaseAnswersOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "uncle");
        await Answer(UrfStep.Witnesses, "own");
        _script["الشهود مش هييجوا"] = new("other", null, "unknown", null, 0.8, CaseSaid: [Took(UrfStep.Witnesses, "own")]);
        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(await Say("الشهود مش هييجوا")));
        Assert.Equal((UrfCase.Collecting, UrfStep.Witnesses, (string?)null), (TheCase.State, TheCase.Step, TheCase.Witnesses));
    }

    [Fact]
    public async Task Without_their_texts_approved_a_question_goes_on_as_before()
    {
        UrfFactsOn(); // the v1.84 texts are not approved
        await ToTheWifePast();
        _script["لو هي مطلقة ينفع؟"] = new("other", null, "unknown", null, 0.8, CaseSaid: [Asked(UrfStep.WifePast, "divorced")]);
        Assert.DoesNotContain(TextKeys.UrfOptionCan, Keys(await Say("لو هي مطلقة ينفع؟")));
    }

    [Theory]
    [InlineData("wife_past", "widow", UrfAskAnswer.OptionCan)]
    [InlineData("witnesses", "office", UrfAskAnswer.OptionCan)]
    [InlineData("guardian", "uncle", UrfAskAnswer.GuardianCan)]
    [InlineData("guardian", "other", UrfAskAnswer.GuardianCannot)]
    [InlineData("guardian", "none", UrfAskAnswer.GuardianNone)]
    public void A_question_is_tried_on_a_copy_by_the_same_rules(string field, string value, string what)
    {
        var c = new UrfCase(Guid.NewGuid(), Guid.NewGuid(), UrfCase.Marriage, UrfCase.Collecting, UrfStep.Guardian, 30, 25, "first");
        Assert.Equal(what, UrfRules.Try(c, Asked(field, value))!.What);
        Assert.Equal(UrfCase.Collecting, c.State); // the case itself never changes
    }

    [Fact]
    public void A_guardian_question_for_a_divorced_wife_or_a_divorce_case_has_nothing_to_answer()
    {
        var divorced = new UrfCase(Guid.NewGuid(), Guid.NewGuid(), UrfCase.Marriage, UrfCase.Collecting, UrfStep.Witnesses, 30, 25, "divorced");
        Assert.Null(UrfRules.Try(divorced, Asked(UrfStep.Guardian, "father")));
        var divorce = UrfCase.Start(Guid.NewGuid(), UrfCase.Divorce);
        Assert.Null(UrfRules.Try(divorce, Asked(UrfStep.Witnesses, "own")));
    }

    [Fact]
    public void The_latest_item_of_a_field_wins_and_other_fields_are_all_taken()
    {
        var f = UrfFacts.Of([SaidItem(UrfStep.Guardian, "brother"), Took(UrfStep.Guardian, "brother"), SaidItem(UrfStep.Witnesses, "office"),
            Asked(UrfStep.WifePast, "khul")]);
        Assert.Equal(((string?)null, "office", "khul"), (f.Guardian, f.Witnesses, f.Asked?.Value));
        Assert.Equal([UrfStep.Guardian], f.Withdrawn);
        var later = UrfFacts.Of([Asked(UrfStep.Guardian, "father"), SaidItem(UrfStep.Guardian, "father")]);
        Assert.Equal(("father", (UrfSaid?)null), (later.Guardian, later.Asked)); // he asked, then said it: said
    }
}
