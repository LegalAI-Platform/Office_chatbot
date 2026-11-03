using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Understanding;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.52 (phase 1): with understanding v2 on, the test chats of 2026-11-02 / 2026-11-04 are answered as a person
/// would: «انا بسال بس» stops the Urfi questions, «الاتنين تلاتين» is read as the ages, «غالي» gets the value and the cheaper
/// way (then only «تحت أمرك»), and the other questions of one message get their answers. Off, Hazem is exactly as before.
/// </summary>
public sealed partial class BookingFlowTests
{
    private void UnderstandingOn()
    {
        _settings[UnderstandingV2.SwitchKey] = "true";
        _settings[OfficeInfo.AddressKey] = "1 شارع التحرير";
        foreach (var (k, v) in new Dictionary<string, string>
                 {
                     [TextKeys.NoPressure] = "تحت أمرك يا فندم 🙏 ولو حبيت تكمّل في أي وقت، اكتبلي هنا وأنا معاك.",
                     [TextKeys.PriceObjection] = "فاهم حضرتك تمامًا 🙏 … بـ500 جنيه ربع ساعة.",
                     [TextKeys.OfficePrivacy] = "أكيد يا فندم 🔒 كل كلام حضرتك مع المكتب سري تمامًا.",
                     [TextKeys.OfficeAddress] = "📍 عنوان المكتب: {{address}}.",
                     [TextKeys.AboutLawyer] = "المستشار سعد فتحي سعد…",
                 })
        {
            _texts[k] = new ApprovedText(k, 1, v, 600, true);
        }
    }

    [Fact]
    public async Task The_test_chat_just_asking_stops_the_Urfi_questions_and_they_never_come_back_by_themselves()
    {
        UrfOn();
        UnderstandingOn();
        await ToTheOffer();
        _script["لو هتجوز عرفي في خصوصيه؟"] = new("office_info", "urf_marriage", "unknown", null, 0.8, "urfi", "urf_marriage", InfoTopic: "privacy");
        Assert.Equal([TextKeys.OfficePrivacy, TextKeys.UrfAskAges], Keys(await Say("لو هتجوز عرفي في خصوصيه؟")));

        _script["انا بسال بس"] = new("service_by_name", "urf_marriage", "unknown", null, 0.7, "urfi", "urf_marriage", Stance: "just_asking");
        Assert.Equal([TextKeys.NoPressure], Keys(await Say("انا بسال بس")));

        _script["هو اي رسالتي اصلا"] = new("other", null, "unknown", null, 0.7);
        var d = await Say("هو اي رسالتي اصلا");
        Assert.DoesNotContain(d.Effects!.Replies, r => HazemResponder.UrfTextKeys.Contains(r.Key)); // no «سن الزوج كام» by itself
    }

    [Fact]
    public async Task The_ages_written_in_words_are_read_and_under_18_in_words_is_refused()
    {
        UrfOn();
        UnderstandingOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        Assert.Equal([TextKeys.UrfAskWifePast], Keys(await Say("الاتنين تلاتين")));
        Assert.Equal((30, 30), (TheCase.HusbandAge, TheCase.WifeAge));
    }

    [Fact]
    public async Task Seventeen_in_words_is_under_18()
    {
        UrfOn();
        UnderstandingOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        Assert.Equal([TextKeys.UrfUnderAge], Keys(await Say("الزوج خمسة وعشرين والزوجة سبعتاشر")));
    }

    [Theory]
    [InlineData("خمسة وعشرين", "25")]
    [InlineData("خمس و عشرين", "25")]
    [InlineData("سبعة عشر", "17")]
    [InlineData("تمنتاشر", "18")]
    [InlineData("ثلاثين", "30")]
    [InlineData("انا ست", "انا ست")] // «ست» alone is a woman, never 6
    public void Number_words_become_digits(string text, string expected) => Assert.Equal(expected, NumberWords.ToDigits(text));

    [Fact]
    public async Task Too_dear_gets_the_value_and_the_cheaper_way_once_then_only_no_pressure()
    {
        UnderstandingOn();
        await ToTheOffer();
        _script["غالية اوي"] = new("other", null, "unknown", null, 0.8, Stance: "price_objection");
        var first = await Say("غالية اوي");
        Assert.Equal([TextKeys.PriceObjection], Keys(first));
        Assert.Empty(first.Effects!.Tasks); // the owner: «دا بيسال عادي»
        _script["لا مش عايز انتو غاليين"] = new("thanks_or_closing", null, "unknown", null, 0.8, Stance: "price_objection");
        Assert.Equal([TextKeys.NoPressure], Keys(await Say("لا مش عايز انتو غاليين")));
    }

    [Fact]
    public async Task Every_office_question_of_one_message_gets_its_answer()
    {
        UnderstandingOn();
        await ToTheOffer();
        _script["عايز اعرف عنوان المكتب وعايز اعرف مين استاذ سعد"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "address",
            AlsoAsked: ["lawyer"]);
        var keys = Keys(await Say("عايز اعرف عنوان المكتب وعايز اعرف مين استاذ سعد"));
        Assert.Contains(TextKeys.OfficeAddress, keys);
        Assert.Contains(TextKeys.AboutLawyer, keys);
    }

    [Fact]
    public async Task With_the_switch_off_Hazem_is_as_before()
    {
        UnderstandingOn();
        _settings[UnderstandingV2.SwitchKey] = "false";
        await ToTheOffer();
        _script["غالية اوي"] = new("other", null, "unknown", null, 0.8, Stance: "price_objection");
        Assert.DoesNotContain(TextKeys.PriceObjection, Keys(await Say("غالية اوي")));
    }
}
