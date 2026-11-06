using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.86 (Ahmed Amin's chat, 2026-11-06): «هو لازم حد يجي معاها من اهلها؟» under the guardian list got the closing
/// «نعتذر لحضرتك… لو محتاج أي خدمة تانية» while his case was open. Asking whether a guardian is needed is asking about going
/// without one (classify-v31: guardian asked none): the rule is said as an answer and the question waiting comes again.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string NeededWords = "هو لازم حد يجي معاها من اهلها؟";

    private void GuardianNeededOn() =>
        _texts[TextKeys.UrfGuardianNeeded] = new ApprovedText(TextKeys.UrfGuardianNeeded, 1,
            "أيوه، لو دي أول جوازة للزوجة لازم يكون معاها حد من أهلها موافق: الأب أو الأخ أو العم.", 300, true);

    [Fact]
    public async Task Asking_whether_a_guardian_is_needed_is_answered_and_the_guardian_question_comes_again()
    {
        CaseAnswersOn();
        GuardianNeededOn();
        await ToTheGuardian();
        _script[NeededWords] = new("legal_question", "urf_marriage", "unknown", null, 0.8, CaseSaid: [Asked(UrfStep.Guardian, "none")]);
        var d = await Say(NeededWords);
        Assert.Equal([TextKeys.UrfGuardianNeeded, TextKeys.UrfAskGuardian], Keys(d)); // never the closing text, never the office
        Assert.Equal((UrfCase.Collecting, UrfStep.Guardian, (string?)null), (TheCase.State, TheCase.Step, TheCase.Guardian));
    }

    [Fact]
    public async Task Asked_later_with_the_guardian_given_the_rule_comes_with_the_question_waiting()
    {
        CaseAnswersOn();
        GuardianNeededOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        _script[NeededWords] = new("other", "urf_marriage", "unknown", null, 0.8, CaseSaid: [Asked(UrfStep.Guardian, "none")]);
        Assert.Equal([TextKeys.UrfGuardianNeeded, TextKeys.UrfAskWitnesses], Keys(await Say(NeededWords)));
        Assert.Equal("brother", TheCase.Guardian); // a question changes nothing
    }

    [Fact]
    public async Task Without_the_text_approved_the_policy_is_said_whole_as_v1_84()
    {
        CaseAnswersOn();
        await ToTheGuardian();
        _script[NeededWords] = new("other", null, "unknown", null, 0.8, CaseSaid: [Asked(UrfStep.Guardian, "none")]);
        Assert.Equal([TextKeys.UrfGuardianClosed], Keys(await Say(NeededWords)));
    }
}
