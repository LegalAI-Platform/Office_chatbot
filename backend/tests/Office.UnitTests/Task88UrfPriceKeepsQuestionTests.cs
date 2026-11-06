using Office.Application.Booking;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.88 (the v1.87 replay, the owner: «اعمله»): «بكام؟» under the guardian question got the price with «ابدأ الطلب» and
/// the question never came back, so «منا قولتلك اول جوازه» after it was no longer read as the Urfi topic («ممكن توضّحلي أكتر»).
/// The Urfi price asked while its questions are open is answered as any other question there: the price, then the question waiting.
/// DECISIONS v1.89 (the v1.88 replay, the owner: «صلحه»): that question after the answer was counted as sent again, so «منا قولتلك
/// اول جوازه» reached the office. After an answer in the same reply, the question comes back as it is and is not counted.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string HowMuch = "بكام؟";
    private const string ToldYou = "منا قولتلك اول جوازه";

    private void UrfPriceOn()
    {
        ApproveNoLoopTexts(); // «علشان نكمّل…» and «هبلّغ المكتب» approved, as on the server
        ServicesOn();
        _texts[TextKeys.BtnStartOrder] = new ApprovedText(TextKeys.BtnStartOrder, 1, "✅ ابدأ الطلب", 20, true);
        _texts[TextKeys.PriceUrf] = new ApprovedText(TextKeys.PriceUrf, 1, "T:price_urf", 600, true);
        _script[HowMuch] = new("price_question", "urf_marriage", "unknown", null, 0.9);
        _script[ToldYou] = new("other", "urf_marriage", "unknown", null, 0.8);
    }

    [Fact]
    public async Task The_urfi_price_asked_under_its_questions_is_said_and_the_question_waiting_comes_back()
    {
        CaseAnswersOn();
        UrfPriceOn();
        await ToTheGuardian();
        var d = await Say(HowMuch);
        Assert.Equal([TextKeys.PriceUrf, TextKeys.UrfAskGuardian], Keys(d)); // v1.89: as it is, after the answer
        Assert.DoesNotContain(d.Actions, a => a.Interactive?.Choices.Any(c => c.Id == ServiceRow.Id(ServiceRow.Urf)) == true); // no «ابدأ الطلب»
        Assert.Equal((UrfCase.Collecting, UrfStep.Guardian), (TheCase.State, TheCase.Step));

        // what he said before, said again, leaves the question unanswered: the first time it comes back in other words
        Assert.Equal([TextKeys.UrfStepAgain, TextKeys.UrfAskGuardian], Keys(await Say(ToldYou)));

        var third = await Say(ToldYou); // the second time unanswered: the office takes it, as before
        Assert.Equal([TextKeys.UnclearAgain], Keys(third));
    }

    [Fact]
    public async Task A_legal_question_under_the_urfi_questions_brings_the_question_back_as_it_is()
    {
        CaseAnswersOn();
        UrfPriceOn();
        await ToTheGuardian();
        _script["هل الشهود هيتكتمو على الزواج"] = new("legal_question", null, "unknown", null, 0.9, "urfi", "urf_marriage");
        Assert.Equal([TextKeys.UrfQuestionLater, TextKeys.UrfAskGuardian], Keys(await Say("هل الشهود هيتكتمو على الزواج")));
        Assert.Equal([TextKeys.UrfStepAgain, TextKeys.UrfAskGuardian], Keys(await Say(ToldYou)));
    }

    [Fact]
    public async Task With_the_switch_off_the_urfi_price_under_its_questions_is_as_v1_81()
    {
        UrfFactsOn(); // understanding v2 on, hazem.urf_case_said off
        GuardianCanOn();
        UrfPriceOn();
        await ToTheGuardian();
        var d = await Say(HowMuch);
        Assert.Equal([TextKeys.PriceUrf], Keys(d));
        Assert.Contains(d.Actions, a => a.Interactive?.Choices.Any(c => c.Id == ServiceRow.Id(ServiceRow.Urf)) == true);
    }
}
