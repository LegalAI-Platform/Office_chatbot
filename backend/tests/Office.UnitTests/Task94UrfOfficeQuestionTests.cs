using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.94 (the v1.93 replay, the owner: «صلّحه»): «هو انا بكلم حد حقيقي دلوقتي؟» under the ages question got who Hazem
/// is with the welcome's buttons, and the ages question never came back. A question about Hazem or the office in the middle of
/// the Urfi questions is answered as any other question there: its answer, then the question waiting. Switch off: as v1.81.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string RealPerson = "هو انا بكلم حد حقيقي دلوقتي؟";

    private async Task AtTheAges()
    {
        UrfOn();
        _texts[TextKeys.AboutHazem] = new ApprovedText(TextKeys.AboutHazem, 1, "أنا حازم، المساعد الرقمي لمكتب المستشار سعد فتحي سعد للمحاماة. تحت أمرك، محتاج إيه؟", 300, true);
        await ToTheOffer();
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say("عايز أعمل جواز عرفي")));
        _script[RealPerson] = new("office_info", "urf_marriage", "unknown", null, 0.85, InfoTopic: "assistant");
    }

    [Fact]
    public async Task Who_hazem_is_asked_under_the_urfi_questions_is_said_and_the_question_waiting_comes_back()
    {
        CaseAnswersOn();
        await AtTheAges();
        var d = await Say(RealPerson);
        Assert.Equal([TextKeys.AboutHazem, TextKeys.UrfAskAges], Keys(d));
        Assert.DoesNotContain(d.Actions, a => a.Interactive is not null); // not the welcome's buttons: the ages are typed
        Assert.Equal((UrfCase.Collecting, UrfStep.Ages), (TheCase.State, TheCase.Step));
    }

    [Fact]
    public async Task With_the_switch_off_who_hazem_is_under_the_urfi_questions_is_as_v1_81()
    {
        UrfFactsOn(); // understanding v2 on, hazem.urf_case_said off
        await AtTheAges();
        Assert.DoesNotContain(TextKeys.UrfAskAges, Keys(await Say(RealPerson)));
    }
}
