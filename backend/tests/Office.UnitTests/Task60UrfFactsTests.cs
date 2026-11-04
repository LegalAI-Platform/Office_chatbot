using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Understanding;
using Office.Application.Urf;
using Office.Application.Writing;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.60 (the test chats of 2026-11-04): «كنت عايز اتجوز عرفي الزوجه مطلقه» was asked «أول جوازة ولا اتطلقت؟» a minute
/// later, and «عايز الشهود من المكتب» got the price and documents of his OWN witnesses again. What the client says about the case
/// in his own words is kept, the questions it answers are not asked, and a change of mind before the time is picked works the
/// price and documents out again.
/// </summary>
public sealed partial class BookingFlowTests
{
    private void UrfFactsOn()
    {
        UrfOn();
        _settings[UnderstandingV2.SwitchKey] = "true";
    }

    [Fact]
    public async Task What_he_says_in_the_first_message_is_not_asked_again()
    {
        UrfFactsOn();
        await ToTheOffer();
        _script["كنت عايز اتجوز عرفي الزوجه مطلقه"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9, UrfWifePast: "divorced");
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say("كنت عايز اتجوز عرفي الزوجه مطلقه")));
        Assert.Equal("divorced", TheCase.WifePast);

        _script["25 و 28"] = new("other", null, "unknown", null, 0.9);
        Assert.Equal([TextKeys.UrfAskGuardian], Keys(await Say("25 و 28"))); // her past is known: straight to the guardian
        Assert.Equal("divorced", TheCase.WifePast);
    }

    [Fact]
    public async Task Everything_said_up_front_goes_straight_to_the_price_once_the_ages_are_in()
    {
        UrfFactsOn();
        await ToTheOffer();
        _script["عايز اتجوز عرفي وأخوها موافق والشهود هنجيبهم واول جوازة"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9,
            UrfWifePast: "first", UrfGuardian: "brother", UrfWitnesses: "own");
        await Say("عايز اتجوز عرفي وأخوها موافق والشهود هنجيبهم واول جوازة");
        _script["30 و 25"] = new("other", null, "unknown", null, 0.9);
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Say("30 و 25")));
        Assert.Equal(UrfCase.DocumentsPending, TheCase.State);
    }

    [Fact]
    public async Task Choosing_the_other_witnesses_in_words_changes_the_price_and_the_documents()
    {
        UrfFactsOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        var own = await Answer(UrfStep.Witnesses, "own");
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(own));
        Assert.Contains("بطاقتين الشاهدين", own.Actions[0].Text);

        _script["عايز الشهود من المكتب"] = new("service_by_name", "urf_marriage", "unknown", null, 0.85, UrfWitnesses: "office");
        var office = await Say("عايز الشهود من المكتب");
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(office));
        Assert.Equal("office", TheCase.Witnesses);
        Assert.Contains("الشهود من المكتب", office.Actions[0].Text);
        Assert.Contains("5", office.Actions[0].Text); // 5,000, not the 4,000 of his own witnesses
        Assert.DoesNotContain("بطاقتين الشاهدين", office.Actions[0].Text);
    }

    [Fact]
    public async Task A_question_that_only_asks_about_the_office_is_answered_without_the_consultation_offer()
    {
        UnderstandingOn();
        await ToTheOffer();
        _script["اي الفروع بتاعتكم"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "address");
        Assert.Equal([TextKeys.OfficeAddress], Keys(await Say("اي الفروع بتاعتكم")));
        _settings[UnderstandingV2.SwitchKey] = "false"; // off: as before, the offer follows the answer
        Assert.Equal(TextKeys.OfficeAddress, Keys(await Say("اي الفروع بتاعتكم"))[0]);
        Assert.True(Keys(await Say("اي الفروع بتاعتكم")).Count >= 1);
    }

    [Fact]
    public async Task A_sentence_for_his_question_comes_before_the_address_which_stays_to_the_letter()
    {
        FreeAnswerOn();
        _settings[Office.Application.Writing.Conversational.InfoSwitchKey] = "true";
        await ToTheOffer();
        _script["ليكم فرع في اسكندريه"] = new("office_info", null, "unknown", null, 0.85, InfoTopic: "address");
        _writer.Reply = "للمكتب مقر واحد بس، ومفيش فرع في إسكندرية 🙏";
        var d = await Say("ليكم فرع في اسكندريه");
        Assert.Equal([TextKeys.OfficeAddress], Keys(d));
        Assert.Equal(_writer.Reply + "\n📍 عنوان المكتب: 1 شارع التحرير.", d.Actions[^1].Text);
        Assert.Equal(WrittenReply.Passed, d.Effects!.Written!.Guard);
    }

    [Fact]
    public async Task A_sentence_that_says_the_address_itself_never_goes_the_approved_text_does()
    {
        FreeAnswerOn();
        _settings[Office.Application.Writing.Conversational.InfoSwitchKey] = "true";
        await ToTheOffer();
        _script["ليكم فرع في اسكندريه"] = new("office_info", null, "unknown", null, 0.85, InfoTopic: "address");
        _writer.Reply = "عنوان المكتب 1 شارع التحرير وسط البلد";
        var d = await Say("ليكم فرع في اسكندريه");
        Assert.Equal("📍 عنوان المكتب: 1 شارع التحرير.", d.Actions[^1].Text);
        Assert.Equal("tail_repeated", d.Effects!.Written!.Guard);
    }

    [Fact]
    public async Task With_the_info_switch_off_the_address_goes_alone_as_before()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _script["ليكم فرع في اسكندريه"] = new("office_info", null, "unknown", null, 0.85, InfoTopic: "address");
        var d = await Say("ليكم فرع في اسكندريه");
        Assert.Equal("📍 عنوان المكتب: 1 شارع التحرير.", d.Actions[^1].Text);
        Assert.Null(d.Effects!.Written);
    }

    [Fact]
    public async Task With_the_switch_off_words_change_nothing()
    {
        UrfFactsOn();
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        await Answer(UrfStep.Witnesses, "own");
        _settings[UnderstandingV2.SwitchKey] = "false";
        _script["عايز الشهود من المكتب"] = new("service_by_name", "urf_marriage", "unknown", null, 0.85, UrfWitnesses: "office");
        await Say("عايز الشهود من المكتب");
        Assert.Equal("own", TheCase.Witnesses);
    }
}

public sealed class UrfFactsRuleTests
{
    private static UrfCase Marriage(string state = UrfCase.Collecting, string? step = UrfStep.Ages) =>
        UrfCase.Start(Guid.NewGuid(), UrfCase.Marriage) with { State = state, Step = step };

    [Fact]
    public void While_collecting_a_fact_fills_only_what_was_not_answered()
    {
        var c = Marriage() with { HusbandAge = 30, WifeAge = 25, WifePast = "first", Step = UrfStep.Guardian };
        var said = UrfRules.ApplyFacts(c, "divorced", "brother", "office");
        Assert.Equal("first", said.WifePast); // an answer already given is never overwritten here
        Assert.Equal("brother", said.Guardian);
        Assert.Equal("office", said.Witnesses);
        Assert.Equal(UrfCase.DocumentsPending, said.State); // nothing left to ask: the price can go
    }

    [Fact]
    public void The_step_waiting_moves_past_what_was_said()
    {
        var c = Marriage(step: UrfStep.WifePast) with { HusbandAge = 30, WifeAge = 25 };
        Assert.Equal(UrfStep.Guardian, UrfRules.ApplyFacts(c, "khul", null, null).Step);
    }

    [Fact]
    public void The_ages_are_always_asked_and_nobody_present_is_never_a_fact()
    {
        var said = UrfRules.ApplyFacts(Marriage(), "first", "none", "own");
        Assert.Equal(UrfStep.Ages, said.Step);
        Assert.Null(said.Guardian);
        Assert.Equal("own", said.Witnesses);
    }

    [Fact]
    public void Values_outside_the_lists_are_ignored_and_a_divorce_takes_only_the_witnesses()
    {
        var divorce = UrfCase.Start(Guid.NewGuid(), UrfCase.Divorce);
        Assert.Equal(divorce, UrfRules.ApplyFacts(divorce, "first", "father", "maybe"));
        Assert.Equal("office", UrfRules.ApplyFacts(divorce, "first", "father", "office").Witnesses);
    }

    [Fact]
    public void Before_a_time_is_picked_a_different_fact_changes_the_answer_and_after_it_the_office_has_the_case()
    {
        var waiting = Marriage(UrfCase.DocumentsPending, null) with { HusbandAge = 30, WifeAge = 25, WifePast = "first", Guardian = "father", Witnesses = "own" };
        var changed = UrfRules.ApplyFacts(waiting, null, "brother", "office");
        Assert.Equal(("brother", "office"), (changed.Guardian, changed.Witnesses));
        Assert.Equal(waiting, UrfRules.ApplyFacts(waiting, "first", "father", "own")); // the same: nothing changes

        var held = waiting with { AppointmentId = Guid.NewGuid() };
        Assert.Equal(held, UrfRules.ApplyFacts(held, null, null, "office"));
        var booked = waiting with { State = UrfCase.Booked };
        Assert.Equal(booked, UrfRules.ApplyFacts(booked, null, null, "office"));
    }
}

public sealed class RetryFeedbackTests
{
    [Fact]
    public void A_dropped_price_names_the_numbers_to_the_writer()
    {
        var told = Office.Application.Writing.ReplyRetry.Feedback("price_dropped", "رد", "500، 1,000");
        Assert.Contains("500، 1,000", told);
        Assert.Contains("dropped the price", told);
        Assert.DoesNotContain("These numbers", Office.Application.Writing.ReplyRetry.Feedback("price_dropped", "رد"));
    }
}
