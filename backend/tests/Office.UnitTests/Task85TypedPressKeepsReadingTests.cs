using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.85 (Ahmed Amin's chat on the test server, 2026-11-06): under «أول جوازة / اتطلقت / أرملة» he typed «اول جوازه
/// وهجيب معاها اخوها والشهود هيبقوا صحابي». The AI picked «أول جوازة» and read the brother and the witnesses too, but the code
/// answered the press alone and threw the rest away, so the guardian and the witnesses were asked again («منا قولتلك»). Typed
/// words that pick a choice are that press AND everything else the AI read in them (switch hazem.urf_case_said on).
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string AminWords = "اول جوازه وهجيب معاها اخوها والشهود هيبقوا صحابي";

    [Fact]
    public async Task Typed_words_that_pick_her_past_keep_the_guardian_and_the_witnesses_said_with_it()
    {
        CaseAnswersOn();
        await ToTheWifePast();
        _script[AminWords] = new("service_by_name", "urf_marriage", "unknown", null, 0.88, Picked: 1,
            CaseSaid: [SaidItem(UrfStep.WifePast, "first"), SaidItem(UrfStep.Guardian, "brother"), SaidItem(UrfStep.Witnesses, "own")]);
        var d = await Say(AminWords);
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(d)); // the price and the times at once: no guardian or witnesses question
        Assert.Equal(("first", "brother", "own"), (TheCase.WifePast, TheCase.Guardian, TheCase.Witnesses));
    }

    [Fact]
    public async Task Typed_words_that_pick_her_past_with_a_question_get_the_question_answered()
    {
        CaseAnswersOn();
        await ToTheWifePast();
        const string words = "اول جوازه، ينفع ابوها؟";
        _script[words] = new("service_by_name", "urf_marriage", "unknown", null, 0.85, Picked: 1,
            CaseSaid: [SaidItem(UrfStep.WifePast, "first"), Asked(UrfStep.Guardian, "father")]);
        var d = await Say(words);
        Assert.Equal([TextKeys.UrfGuardianCan], Keys(d)); // «أيوه، الأب ينفع يبقى الولي. هو هيحضر معاكم وموافق؟»
        Assert.Equal(("first", (string?)null), (TheCase.WifePast, TheCase.Guardian)); // a question changes nothing
    }

    [Fact]
    public async Task With_the_switch_off_typed_words_that_pick_a_choice_are_the_press_alone_as_v1_81()
    {
        UrfFactsOn(); // understanding v2 on, hazem.urf_case_said off
        await ToTheWifePast();
        _script[AminWords] = new("service_by_name", "urf_marriage", "unknown", null, 0.88, Picked: 1,
            CaseSaid: [SaidItem(UrfStep.WifePast, "first"), SaidItem(UrfStep.Guardian, "brother"), SaidItem(UrfStep.Witnesses, "own")]);
        Assert.Equal([TextKeys.UrfAskGuardian], Keys(await Say(AminWords)));
        Assert.Null(TheCase.Guardian);
    }

    [Fact]
    public void The_title_with_words_more_is_left_to_the_ai_when_the_case_is_one_list()
    {
        InteractiveChoice[] past = [new("urf:wife_past:first", "أول جوازة"), new("urf:wife_past:divorced", "اتطلقت أو اختلعت"), new("urf:wife_past:widow", "أرملة")];
        Assert.Equal("urf:wife_past:first", TypedChoice.ByWords(past, "اول جوازه واخوها جاي", guess: false)?.Id); // as before
        Assert.Null(TypedChoice.ByWords(past, "اول جوازه واخوها جاي", guess: false, more: false)); // the AI reads the rest
        Assert.Equal("urf:wife_past:first", TypedChoice.ByWords(past, "أول جوازة", guess: false, more: false)?.Id); // the title alone still is
        Assert.Equal("urf:wife_past:widow", TypedChoice.ByWords(past, "3", guess: false, more: false)?.Id); // and its number
    }
}
