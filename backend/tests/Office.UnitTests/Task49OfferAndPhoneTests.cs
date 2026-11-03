using Office.Application.Booking;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Domain.Booking;

namespace Office.UnitTests;

/// <summary>
/// v1.49 (B-23, the owner): the consultation offer says once a day that there is an urgent call (within minutes by day, when
/// the working hours start by night) and has it as a third button; a normal phone call from the office's number is a third app.
/// </summary>
public sealed partial class BookingFlowTests
{
    private void V149On()
    {
        UrgentOn();
        foreach (var (k, v) in new Dictionary<string, string>
                 {
                     [TextKeys.ConsultationOfferUrgentDay] = "عرض + عاجل خلال دقايق لو فاضي",
                     [TextKeys.ConsultationOfferUrgentNight] = "عرض + عاجل الساعة {{opens}} الصبح",
                     [TextKeys.BtnBookUrgent] = "🚨 استشارة عاجلة",
                     [TextKeys.AskCallAppPhone] = "واتساب أو FaceTime أو مكالمة عادية",
                     [TextKeys.BtnAppPhone] = "📞 مكالمة عادية",
                     [TextKeys.BtnAppsList] = "اختار",
                 })
        {
            _texts[k] = new ApprovedText(k, 1, v, k.StartsWith("btn_", StringComparison.Ordinal) ? 20 : 300, true);
        }

        _booking.Schedule = new Schedule(AvailabilityTests.Every(600, 1440), []); // 10:00 to midnight
    }

    [Fact]
    public async Task V149_by_day_the_offer_says_the_urgent_call_comes_within_minutes()
    {
        V149On();
        var offer = (await ToTheOffer()).Actions.Single(a => a.Type == ActionType.SendInteractive);
        Assert.Equal("عرض + عاجل خلال دقايق لو فاضي", offer.Text);
        Assert.Equal(BookingChoice.Mode(BookingService.Office), offer.Interactive!.Choices[1].Id); // the two kinds stay first
    }

    [Fact]
    public async Task V149_by_night_the_offer_says_the_urgent_call_comes_when_the_working_hours_start()
    {
        V149On();
        _now = AvailabilityTests.At(19, 2); // 02:00, the hours start at 10:00
        var offer = (await ToTheOffer()).Actions.Single(a => a.Type == ActionType.SendInteractive);
        Assert.Equal("عرض + عاجل الساعة 10:00 الصبح", offer.Text);
    }

    [Fact]
    public async Task V149_without_its_texts_the_offer_is_as_before_with_its_two_buttons()
    {
        var offer = (await ToTheOffer()).Actions.Single(a => a.Type == ActionType.SendInteractive);
        Assert.Equal(2, offer.Interactive!.Choices.Count); // a missing text never drops the buttons
    }

    [Fact]
    public async Task V149_an_egyptian_number_can_pick_a_normal_phone_call_and_keeps_it_with_the_booking()
    {
        V149On();
        await ToTheOffer();
        var ask = Assert.Single((await Press(BookingChoice.Mode(BookingService.PhoneVideo))).Actions);
        Assert.Equal("واتساب أو FaceTime أو مكالمة عادية", ask.Text);
        Assert.Equal([BookingChoice.Call(CallApp.WhatsApp), BookingChoice.Call(CallApp.FaceTime), BookingChoice.Call(CallApp.Phone)],
            ask.Interactive!.Choices.Select(c => c.Id));

        var times = Assert.Single((await Press(BookingChoice.Call(CallApp.Phone))).Actions).Interactive!;
        await Press(times.Choices[0].Id);
        Assert.Equal(CallApp.Phone, Assert.Single(_booking.Appointments).CallApp);
    }

    [Fact]
    public async Task V149_four_apps_go_as_a_list()
    {
        V149On();
        _settings[OfficeInfo.MeetLinkKey] = "https://meet.google.com/abc-defg-hij";
        await ToTheOffer();
        var ask = Assert.Single((await Press(BookingChoice.Mode(BookingService.PhoneVideo))).Actions).Interactive!;
        Assert.Equal(InteractivePayload.List, ask.Kind);
        Assert.Equal(4, ask.Choices.Count);
    }

    [Theory]
    [InlineData(CallApp.Phone, "ph", "مكالمة عادية")]
    [InlineData(CallApp.WhatsApp, "wa", "واتساب")]
    public void V149_the_phone_call_has_its_code_and_its_name(string app, string code, string name)
    {
        Assert.Equal(code, CallApp.Short(app));
        Assert.Equal(app, CallApp.ByShort(code));
        Assert.Equal(name, CallApp.Name(app));
    }
}
