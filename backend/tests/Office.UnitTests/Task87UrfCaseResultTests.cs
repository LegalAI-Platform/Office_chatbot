using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.87 (Ahmed Amin's chat, 2026-11-06, the owner: «دا الحل العام؟»): the AI gives what the message makes TRUE about the
/// Urfi case and every question in it, the code no longer judges the order of his words. (1) «ينفع اخلي الشهود من المكتب الشهود
/// عندي مش هيجوا» lost the question to the answer taken back after it: a question is answered whatever is said around it. (2) «اخو
/// العروسه مش هيجي» brought the guardian question back with «الأخ» in it: who will not come is kept, and the question is asked
/// without him. (3) A change is kept only when its words are the client's own.
/// </summary>
public sealed partial class BookingFlowTests
{
    private static UrfSaid NotComing(string who, string? quote = null) => new(UrfStep.Guardian, UrfKind.NotComing, who, Quote: quote);

    private void GuardianOtherOn() =>
        _texts[TextKeys.UrfAskGuardianOther] = new ApprovedText(TextKeys.UrfAskGuardianOther, 1,
            "طيب، مين تاني من أهل الزوجة موجود وموافق: {{options}}؟", 300, true);

    /// <summary>Up to the price and times: first marriage, the brother, his own witnesses.</summary>
    private async Task ToTheTimesWithTheBrother()
    {
        CaseAnswersOn();
        GuardianNeededOn();
        GuardianOtherOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Answer(UrfStep.Witnesses, "own")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // his order does not matter
    public async Task A_question_with_an_answer_taken_back_on_the_same_field_is_still_answered(bool questionLast)
    {
        await ToTheTimesWithTheBrother();
        const string words = "ينفع اخلي الشهود من المكتب الشهود عندي مش هيجوا";
        UrfSaid took = Took(UrfStep.Witnesses, "own"), asked = Asked(UrfStep.Witnesses, "office");
        _script[words] = new("service_by_name", "urf_marriage", "unknown", null, 0.85, CaseSaid: questionLast ? [took, asked] : [asked, took]);
        var d = await Say(words);
        Assert.Equal([TextKeys.UrfOptionCan, TextKeys.UrfAskWitnesses], Keys(d)); // «أيوه، إن المكتب يوفّر الشهود ينفع ✅», then the question
        Assert.Equal((UrfCase.Collecting, UrfStep.Witnesses, (string?)null), (TheCase.State, TheCase.Step, TheCase.Witnesses));
    }

    [Fact]
    public async Task A_guardian_who_will_not_come_is_left_out_of_the_question_asked_again()
    {
        await ToTheTimesWithTheBrother();
        const string words = "بقولك ايه اخو العروسه مش هيجي لسه مبلغني";
        _script[words] = new("other", "urf_marriage", "unknown", null, 0.8, CaseSaid: [NotComing("brother")]);
        var d = await Say(words);
        Assert.Equal([TextKeys.UrfAskGuardianOther], Keys(d));
        Assert.Contains("الأب، ولا العم", d.Actions[0].Text);
        Assert.Equal(["urf:guardian:father", "urf:guardian:uncle", "urf:guardian:none"], d.Actions[0].Interactive!.Choices.Select(c => c.Id));
        Assert.Equal(((string?)null, "brother"), (TheCase.Guardian, TheCase.GuardianOut));

        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Answer(UrfStep.Guardian, "father")));
        Assert.Equal("father", TheCase.Guardian);
    }

    [Fact]
    public async Task The_one_ruled_out_named_again_as_coming_leaves_the_list()
    {
        await ToTheTimesWithTheBrother();
        _script["اخوها مش هيجي"] = new("other", "urf_marriage", "unknown", null, 0.8, CaseSaid: [NotComing("brother")]);
        await Say("اخوها مش هيجي");
        _script["لا خلاص اخوها جاي"] = new("other", "urf_marriage", "unknown", null, 0.8, CaseSaid: [SaidItem(UrfStep.Guardian, "brother")]);
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Say("لا خلاص اخوها جاي")));
        Assert.Equal(("brother", (string?)null), (TheCase.Guardian, TheCase.GuardianOut));
    }

    [Fact]
    public async Task None_of_the_three_left_is_nobody_of_her_family()
    {
        await ToTheTimesWithTheBrother();
        _script["لا اخوها ولا ابوها ولا عمها هييجوا"] = new("other", "urf_marriage", "unknown", null, 0.8,
            CaseSaid: [NotComing("brother"), NotComing("father"), NotComing("uncle")]);
        await Say("لا اخوها ولا ابوها ولا عمها هييجوا");
        Assert.Equal(UrfCase.RejectedGuardian, TheCase.State); // B-11, as if he said «مفيش حد منهم»
    }

    [Fact]
    public async Task Two_questions_in_one_message_are_both_answered()
    {
        CaseAnswersOn();
        GuardianNeededOn();
        await ToTheGuardian();
        const string words = "لازم حد من اهلها؟ وينفع الشهود من المكتب؟";
        _script[words] = new("other", "urf_marriage", "unknown", null, 0.8,
            CaseSaid: [Asked(UrfStep.Guardian, "none"), Asked(UrfStep.Witnesses, "office")]);
        Assert.Equal([TextKeys.UrfGuardianNeeded, TextKeys.UrfOptionCan, TextKeys.UrfAskGuardian], Keys(await Say(words)));
    }

    [Fact]
    public async Task A_change_whose_words_are_not_his_is_not_taken()
    {
        CaseAnswersOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        _script["طيب"] = new("other", "urf_marriage", "unknown", null, 0.8,
            CaseSaid: [new UrfSaid(UrfStep.Guardian, UrfKind.Said, "father", Quote: "ابوها جاي")]); // he never said it
        await Say("طيب");
        Assert.Equal("brother", TheCase.Guardian);

        _script["ابوها جاي"] = new("other", "urf_marriage", "unknown", null, 0.8,
            CaseSaid: [new UrfSaid(UrfStep.Guardian, UrfKind.Said, "father", Quote: "أبوها جاي")]); // spelling aside, his words
        await Say("ابوها جاي");
        Assert.Equal("father", TheCase.Guardian);
    }

    [Fact]
    public void Only_the_items_quoted_from_his_messages_are_kept()
    {
        UrfSaid mine = new(UrfStep.Witnesses, UrfKind.Withdrawn, "own", Quote: "الشهود عندي مش هيجوا"),
            invented = new(UrfStep.Guardian, UrfKind.Said, "father", Quote: "ابوها"), empty = new(UrfStep.WifePast, UrfKind.Said, "first", Quote: "");
        var kept = UrfFacts.Quoted([mine, invented, empty], ["ينفع اخلي الشهود من المكتب الشهود عندي مش هيجوا"]);
        Assert.Equal([mine], kept);
        Assert.Equal(3, UrfFacts.Quoted([mine, invented, empty], null)!.Count); // no words to check against: as before
    }
}
