using Office.Application.Booking;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Domain.Booking;
using Office.Domain.Common;

namespace Office.UnitTests;

/// <summary>
/// TASK 15 (DECISIONS v1.37, B-02): a client whose WhatsApp has a username comes with a BSUID instead of his number. At the
/// booking (after the name) Hazem asks for the number before holding the time; asked once more; without it a phone call
/// becomes the office meeting offer, and an office meeting goes on without it.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string Bsuid = "EG.15151515151515151515";

    private Task<TurnDecision> SayAs(string text) =>
        Send(new Turn($"wt:{++_turn}", Bsuid, null, [new IncomingMessage($"wamid.{++_turn}", MessageType.Text, text, null, null)], "tester15"));

    private Task<TurnDecision> PressAs(string id) =>
        Send(new Turn($"wt:{++_turn}", Bsuid, null,
            [new IncomingMessage($"wamid.{++_turn}", MessageType.Interactive, null, null, null, Interactive: new Interactive("list_reply", id, "x"))],
            "tester15"));

    /// <summary>The username client («@tester15» in the test list) at the offer, his name given at the time he picks.</summary>
    private async Task<TurnDecision> UsernameClientPicks(string pick)
    {
        _settings[HazemResponder.RecipientsKey] = $"{Number},@Tester15";
        foreach (var (k, v) in new[]
                 {
                     (TextKeys.AskPhone, "رقمك؟"), (TextKeys.AskPhoneAgain, "لو سمحت، رقمك؟"), (TextKeys.PhoneNeededForCall, "من غير رقم… مقابلة؟"),
                 })
        {
            _texts[k] = new ApprovedText(k, 1, v, 300, true);
        }

        _script["السلام عليكم"] = new("greeting", null, "unknown", null, 0.9);
        _script["عايز أحجز"] = new("consultation_request", null, "unknown", null, 0.9, "inheritance");
        _script["أحمد محمود"] = new("provide_name", null, "unknown", "أحمد محمود", 0.95);
        await SayAs("السلام عليكم");
        await SayAs("عايز أحجز");
        Assert.Equal([TextKeys.AskNameBooking], Keys(await PressAs(pick)));
        return await SayAs("أحمد محمود");
    }

    [Fact]
    public async Task V137_a_username_client_is_asked_his_number_after_his_name_and_the_number_holds_the_time()
    {
        var ask = await UsernameClientPicks(NamePick);
        Assert.Equal([TextKeys.AskPhone], Keys(ask)); // not the hold yet
        Assert.Empty(_booking.Appointments);

        var held = await SayAs("0101 234 5678");
        Assert.Equal([TextKeys.BookingHold], Keys(held));
        Assert.Equal("201012345678", _rows[Bsuid].Person.PreferredCallPhone); // the lawyer's number for him
        Assert.Single(_booking.Appointments);
    }

    [Fact]
    public async Task V137_without_a_number_a_phone_call_offers_the_office_meeting_instead()
    {
        await UsernameClientPicks(BookingChoice.Pick(BookingService.PhoneVideo, NameSlot, CallApp.WhatsApp));
        Assert.Equal([TextKeys.AskPhoneAgain], Keys(await SayAs("مش عايز")));
        var instead = await SayAs("لا");
        Assert.Equal([TextKeys.PhoneNeededForCall], Keys(instead));
        Assert.Equal(BookingChoice.Mode(BookingService.Office), Assert.Single(instead.Actions[0].Interactive!.Choices).Id);
        Assert.Empty(_booking.Appointments); // no call held without a number
    }

    [Fact]
    public async Task V137_without_a_number_an_office_meeting_goes_on_and_a_number_client_is_never_asked()
    {
        await UsernameClientPicks(NamePick);
        await SayAs("مش عايز");
        Assert.Equal([TextKeys.BookingHold], Keys(await SayAs("لا"))); // the office meeting needs no number
        Assert.Null(_rows[Bsuid].Person.PreferredCallPhone);

        // A client whose WhatsApp shows his number: never asked.
        await ToTheOfferWithoutName();
        await Press(NamePick);
        _script["أحمد محمود"] = new("provide_name", null, "unknown", "أحمد محمود", 0.95);
        Assert.DoesNotContain(TextKeys.AskPhone, Keys(await Say("أحمد محمود")));
    }

    [Fact]
    public void V137_a_bsuid_is_a_client_key_but_not_a_phone()
    {
        Assert.True(WaId.TryCreate(Bsuid, out var w));
        Assert.False(w.IsPhone);
        Assert.True(WaId.From("201000000088").IsPhone);
        Assert.False(WaId.TryCreate("eg.123", out _));
        Assert.True(HazemResponder.IsTestRecipient(new HazemSettings(new Dictionary<string, string> { [HazemResponder.RecipientsKey] = "@Tester15" }),
            new Turn("t", Bsuid, null, [], "tester15")));
    }
}
