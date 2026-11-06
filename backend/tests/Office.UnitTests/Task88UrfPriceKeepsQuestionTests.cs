using Office.Application.Booking;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.88 (the v1.87 replay, the owner: «اعمله»): «بكام؟» under the guardian question got the price with «ابدأ الطلب» and
/// the question never came back, so «منا قولتلك اول جوازه» after it was no longer read as the Urfi topic («ممكن توضّحلي أكتر»).
/// The Urfi price asked while its questions are open is answered as any other question there: the price, then the question waiting.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string HowMuch = "بكام؟";

    private void UrfPriceOn()
    {
        ServicesOn();
        _texts[TextKeys.BtnStartOrder] = new ApprovedText(TextKeys.BtnStartOrder, 1, "✅ ابدأ الطلب", 20, true);
        _texts[TextKeys.PriceUrf] = new ApprovedText(TextKeys.PriceUrf, 1, "T:price_urf", 600, true);
        _script[HowMuch] = new("price_question", "urf_marriage", "unknown", null, 0.9);
    }

    [Fact]
    public async Task The_urfi_price_asked_under_its_questions_is_said_and_the_question_waiting_comes_back()
    {
        CaseAnswersOn();
        UrfPriceOn();
        await ToTheGuardian();
        var d = await Say(HowMuch);
        Assert.Equal([TextKeys.PriceUrf, TextKeys.UrfAskGuardian], Keys(d));
        Assert.DoesNotContain(d.Actions, a => a.Interactive?.Choices.Any(c => c.Id == ServiceRow.Id(ServiceRow.Urf)) == true); // no «ابدأ الطلب»
        Assert.Equal((UrfCase.Collecting, UrfStep.Guardian), (TheCase.State, TheCase.Step));

        // what he said before, said again: the topic is still the Urfi questions, so the question waiting comes back
        _script["منا قولتلك اول جوازه"] = new("other", "urf_marriage", "unknown", null, 0.8);
        Assert.Equal([TextKeys.UrfAskGuardian], Keys(await Say("منا قولتلك اول جوازه")));
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
