using Office.Application.Booking;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.97 (the owner's chat, 2026-11-08: «💬 أحكي مشكلتي» pressed with «يسطا انا بحبك» typed got «احكيلي الموضوع», his
/// words dropped; Ahmed Amin's: «خدمات المكتب» with «عايز اتجوز عرفي» got the services list): a services button pressed with words
/// typed in the same turn is read by the AI as what he wrote, the button's title first. Switch off: the button alone, as v1.81.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string TellTitle = "💬 أحكي مشكلتي";

    private Task<TurnDecision> PressAndSay(string id, string title, string words) =>
        Send(new Turn($"wt:{++_turn}", Number, null,
        [
            new IncomingMessage($"wamid.{++_turn}", MessageType.Interactive, null, null, null, Interactive: new Interactive("button_reply", id, title)),
            new IncomingMessage($"wamid.{++_turn}", MessageType.Text, words, null, null),
        ]));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Words_typed_with_a_services_button_are_read_with_it(bool switchOn)
    {
        if (switchOn)
        {
            CaseAnswersOn();
        }
        else
        {
            UrfFactsOn(); // understanding v2 on, hazem.urf_case_said off
        }

        UrfOn();
        ServicesOn();
        await ToTheOffer();
        _script[TellTitle + "\nعايز اتجوز عرفي"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9);
        var d = await PressAndSay(ServiceRow.Id(ServiceRow.Tell), TellTitle, "عايز اتجوز عرفي");
        Assert.Equal([switchOn ? TextKeys.UrfAskAges : TextKeys.ServicesTell], Keys(d)); // off: «احكيلي الموضوع», as v1.81
    }

    [Fact]
    public async Task A_services_button_pressed_alone_is_answered_by_the_code_as_before()
    {
        CaseAnswersOn();
        ServicesOn();
        await ToTheOffer();
        Assert.Equal([TextKeys.ServicesTell], Keys(await Press(ServiceRow.Id(ServiceRow.Tell))));
    }
}
