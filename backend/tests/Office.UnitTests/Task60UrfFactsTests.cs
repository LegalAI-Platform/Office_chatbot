using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
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
        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(await Say("25 و 28"))); // her past is known (v1.68: no guardian for a divorced wife)
        Assert.Equal("divorced", TheCase.WifePast);
    }

    [Fact] // v1.72 (Ahmed Amin's chat): «طب هجيب ابوها» after «مفيش حد منهم» got the closing text, «الزوجه معاها الاخ» silence
    public async Task A_guardian_named_after_the_refusal_changes_it_and_the_case_goes_on()
    {
        UrfFactsOn();
        await ToTheGuardian();
        _texts[TextKeys.UrfGuardianClosed] = new ApprovedText(TextKeys.UrfGuardianClosed, 1, "T:guardian closed", 300, true);
        Assert.Equal([TextKeys.UrfNoGuardian], Keys(await Answer(UrfStep.Guardian, "none")));
        _script["ليه"] = new("other", null, "unknown", null, 0.9);
        Assert.Equal([TextKeys.UrfGuardianClosed], Keys(await Say("ليه"))); // nothing changed: the closing text

        _script["طب هجيب ابوها"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9, UrfGuardian: "father");
        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(await Say("طب هجيب ابوها"))); // the next question, not the ages again
        Assert.Equal(2, _booking.UrfCases.Count); // the refused case stays as it was; a new one goes on with its answers
        var again = _booking.UrfCases[^1];
        Assert.Equal((UrfCase.Collecting, "father", "first", 30, 25), (again.State, again.Guardian, again.WifePast, again.HusbandAge, again.WifeAge));
        Assert.Equal(UrfCase.RejectedGuardian, _booking.UrfCases[0].State);

        _script["الشهود هنجيبهم"] = new("other", null, "unknown", null, 0.9, UrfWitnesses: "own");
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Say("الشهود هنجيبهم"))); // the refusal is over: the case goes on
    }

    [Fact]
    public async Task After_the_age_refusal_nothing_said_changes_it()
    {
        UrfFactsOn();
        _texts[TextKeys.UrfAgeClosed] = new ApprovedText(TextKeys.UrfAgeClosed, 1, "T:closed", 300, true);
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        _script["الزوج 22 والزوجه 16"] = new("other", null, "unknown", null, 0.9);
        Assert.Equal([TextKeys.UrfUnderAge], Keys(await Say("الزوج 22 والزوجه 16")));
        _script["طب ابوها موافق"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9, UrfGuardian: "father");
        Assert.Equal([TextKeys.UrfAgeClosed], Keys(await Say("طب ابوها موافق")));
        Assert.Equal(UrfCase.RejectedAge, TheCase.State);
    }

    [Fact] // v1.72: «منا قولتلك» while the ages wait: the ages the AI read from the chat are the answer
    public async Task The_ages_the_ai_read_from_the_chat_answer_the_ages_question()
    {
        UrfFactsOn();
        await ToTheOffer();
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say("عايز أعمل جواز عرفي")));
        _script["منا قولتلك"] = new("other", null, "unknown", null, 0.8, UrfHusbandAge: 40, UrfWifeAge: 20);
        Assert.Equal([TextKeys.UrfAskWifePast], Keys(await Say("منا قولتلك")));
        Assert.Equal((40, 20), (TheCase.HusbandAge, TheCase.WifeAge));
    }

    [Fact]
    public async Task The_ages_the_ai_read_go_through_the_same_under_18_rule()
    {
        UrfFactsOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        _script["زي ما قولتلك"] = new("other", null, "unknown", null, 0.8, UrfHusbandAge: 30, UrfWifeAge: 16);
        Assert.Equal([TextKeys.UrfUnderAge], Keys(await Say("زي ما قولتلك")));
        Assert.Equal(UrfCase.RejectedAge, TheCase.State);
    }

    [Fact]
    public async Task Without_the_ages_from_the_ai_the_question_is_asked_again_as_before()
    {
        UrfFactsOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        _script["منا قولتلك"] = new("other", null, "unknown", null, 0.8, UrfHusbandAge: 40); // one age only: not used
        Assert.Equal([TextKeys.UrfAgesAgain], Keys(await Say("منا قولتلك")));
    }

    [Fact] // v1.72: «دي البطاقة مش التحويل» with an image while the time waits for its receipt
    public async Task An_image_his_words_say_is_not_the_receipt_is_not_taken_as_one()
    {
        UrfFactsOn();
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        _script["[مرفق صورة]\nدي البطاقة مش التحويل"] = new("other", null, "unknown", null, 0.9, FileIs: "other");
        var d = await Receipt(caption: "دي البطاقة مش التحويل");
        Assert.Equal([TextKeys.ReceiptSendImage], Keys(d));
        Assert.Equal(AppointmentInfo.PendingPayment, Assert.Single(_booking.Appointments).State);
    }

    [Fact]
    public async Task An_image_with_words_that_say_it_is_the_transfer_is_the_receipt()
    {
        UrfFactsOn();
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        _script["[مرفق صورة]\nاهو التحويل"] = new("other", null, "unknown", null, 0.9, FileIs: "receipt");
        Assert.Equal([TextKeys.BookingPaid], Keys(await Receipt(caption: "اهو التحويل")));
        Assert.Equal(AppointmentInfo.Confirmed, Assert.Single(_booking.Appointments).State);
    }

    [Fact] // v1.74 (Ahmed Amin's chat): «الأب», then «اقصد لا مفيش حد من اهل الزوجه» got the witnesses question again
    public async Task Saying_no_guardian_after_naming_one_refuses_the_first_marriage()
    {
        UrfFactsOn();
        await ToTheGuardian();
        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(await Answer(UrfStep.Guardian, "father")));
        _script["اقصد لا مفيش حد من اهل الزوجه"] = new("service_by_name", "urf_marriage", "unknown", null, 0.8, UrfGuardian: "none");
        Assert.Equal([TextKeys.UrfNoGuardian], Keys(await Say("اقصد لا مفيش حد من اهل الزوجه")));
        Assert.Equal((UrfCase.RejectedGuardian, "none"), (TheCase.State, TheCase.Guardian));
    }

    [Fact] // v1.74 (Ahmed Amin's chat): «لا قصدي الزوجه 25» was not taken
    public async Task A_corrected_age_is_taken_while_the_questions_go_on()
    {
        UrfFactsOn();
        await ToTheGuardian(); // the ages 30 و 25, a first marriage
        _texts[TextKeys.UrfAnswerCorrected] = new ApprovedText(TextKeys.UrfAnswerCorrected, 1, "تمام، عدّلت {{changed}} ✅", 300, true);
        _script["لا قصدي الزوجه 22"] = new("other", null, "unknown", null, 0.8, UrfHusbandAge: 30, UrfWifeAge: 22);
        var d = await Say("لا قصدي الزوجه 22");
        Assert.Equal((30, 22, UrfStep.Guardian), (TheCase.HusbandAge, TheCase.WifeAge, TheCase.Step));
        // v1.75: said back, then the question waiting plainly (never «علشان نكمّل…»)
        Assert.Equal([TextKeys.UrfAnswerCorrected, TextKeys.UrfAskGuardian], Keys(d));
        Assert.Equal("تمام، عدّلت السن: الزوج 30 سنة والزوجة 22 سنة ✅", d.Actions[0].Text);
    }

    [Fact] // v1.75: every answer has its words; several are joined
    public void What_was_corrected_is_said_in_words_for_every_answer()
    {
        var c = UrfCase.Start(Guid.NewGuid(), UrfCase.Marriage) with { HusbandAge = 30, WifeAge = 25, WifePast = "first", Guardian = "father", Witnesses = "own", Step = null };
        Assert.Equal("إنها اتطلقت أو اختلعت قبل كده", UrfRules.Corrected(c, c with { WifePast = "divorced" }));
        Assert.Equal("إنها أرملة", UrfRules.Corrected(c, c with { WifePast = "widow" }));
        Assert.Equal("الولي يبقى الأخ", UrfRules.Corrected(c, c with { Guardian = "brother" }));
        Assert.Equal("المكتب هيوفّر الشهود", UrfRules.Corrected(c, c with { Witnesses = "office" }));
        Assert.Equal("السن: الزوج 30 سنة والزوجة 22 سنة، والمكتب هيوفّر الشهود", UrfRules.Corrected(c, c with { WifeAge = 22, Witnesses = "office" }));
        Assert.Null(UrfRules.Corrected(c with { Witnesses = null }, c)); // filled for the first time: no correction
        Assert.Null(UrfRules.Corrected(c, c));
    }

    [Fact] // v1.76 (Ahmed Amin's chat): «فيه امان؟» can mean two things: he is asked which, once; the Urfi request goes on
    public async Task A_question_with_two_meanings_is_asked_back_once()
    {
        UrfFactsOn();
        _texts[TextKeys.AskWhichMeaning] = new ApprovedText(TextKeys.AskWhichMeaning, 1, "حضرتك قصدك {{meanings}}؟ 🙏", 300, true);
        await ToTheOffer();
        var meanings = new[] { "إن العقد يبقى قانوني ومضمون", "إن الموضوع يفضل سري" };
        _script["عايز اتجوز عرفي وعايز اعرف فيه امان؟"] = new("office_info", "urf_marriage", "unknown", null, 0.7, InfoTopic: "service_detail", Meanings: meanings);
        var d = await Say("عايز اتجوز عرفي وعايز اعرف فيه امان؟");
        Assert.Equal([TextKeys.AskWhichMeaning, TextKeys.UrfAskAges], Keys(d));
        Assert.Equal("حضرتك قصدك إن العقد يبقى قانوني ومضمون، ولا إن الموضوع يفضل سري؟ 🙏", d.Actions[0].Text);
        Assert.DoesNotContain(PendingTask.ServiceQuestion, TaskKinds(d)); // not sent to the office before he says which

        _script["لسه مش فاهم يعني فيه امان؟"] = new("office_info", "urf_marriage", "unknown", null, 0.7, InfoTopic: "service_detail", Meanings: meanings);
        var again = await Say("لسه مش فاهم يعني فيه امان؟");
        Assert.DoesNotContain(TextKeys.AskWhichMeaning, Keys(again)); // asked once: then as before
        Assert.Contains(PendingTask.ServiceQuestion, TaskKinds(again));
    }

    [Fact] // v1.76: the real model reads that message as the Urfi request itself (service_by_name), with the two meanings
    public async Task A_request_with_an_unclear_question_asks_which_and_starts_the_request()
    {
        UrfFactsOn();
        _texts[TextKeys.AskWhichMeaning] = new ApprovedText(TextKeys.AskWhichMeaning, 1, "حضرتك قصدك {{meanings}}؟ 🙏", 300, true);
        await ToTheOffer();
        _script["عايز اتجوز عرفي وعايز اعرف فيه امان؟"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9,
            Meanings: ["إن العقد يبقى قانوني ومضمون", "إن الموضوع يفضل سري"]);
        Assert.Equal([TextKeys.AskWhichMeaning, TextKeys.UrfAskAges], Keys(await Say("عايز اتجوز عرفي وعايز اعرف فيه امان؟")));
    }

    [Fact] // v1.77 (the owner: «المستشار يشرح»): «قصدي العقد قانوني» after «قصدك إيه؟» is the lawyer's at the appointment
    public async Task After_which_meaning_the_legal_one_is_explained_at_the_appointment_and_the_question_comes_back()
    {
        UrfFactsOn();
        _texts[TextKeys.AskWhichMeaning] = new ApprovedText(TextKeys.AskWhichMeaning, 1, "حضرتك قصدك {{meanings}}؟ 🙏", 300, true);
        _texts[TextKeys.UrfQuestionLater] = new ApprovedText(TextKeys.UrfQuestionLater, 2, "المستشار هيوضح النقطة دي لحضرتك في الميعاد.", 300, true);
        await ToTheOffer();
        _script["عايز اتجوز عرفي وعايز اعرف فيه امان؟"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9,
            Meanings: ["إن العقد يبقى قانوني ومضمون", "إن الموضوع يفضل سري"]);
        await Say("عايز اتجوز عرفي وعايز اعرف فيه امان؟");
        _script["لا قصدي العقد قانوني ولا لا"] = new("legal_question", "urf_marriage", "unknown", null, 0.9);
        var d = await Say("لا قصدي العقد قانوني ولا لا");
        Assert.Equal(TextKeys.UrfQuestionLater, Keys(d)[0]);
        Assert.DoesNotContain(PendingTask.ServiceQuestion, TaskKinds(d));
        Assert.Equal(UrfStep.Ages, TheCase.Step); // the ages question comes back after it
    }

    [Fact] // v1.78: «العقد العرفي بياخد قد ايه» is time or money: asked back, nothing pushed after it (he only asks)
    public async Task How_much_it_takes_is_asked_back_time_or_money()
    {
        UrfFactsOn();
        _texts[TextKeys.AskWhichMeaning] = new ApprovedText(TextKeys.AskWhichMeaning, 1, "حضرتك قصدك {{meanings}}؟ 🙏", 300, true);
        await ToTheOffer();
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say("عايز أعمل جواز عرفي")));
        _script["العقد العرفي بياخد قد ايه"] = new("office_info", "urf_marriage", "unknown", null, 0.9, InfoTopic: "service_detail",
            Stance: "just_asking", Meanings: ["إن العقد بياخد وقت قد ايه", "إن العقد بيتكلف كام"]);
        var d = await Say("العقد العرفي بياخد قد ايه");
        Assert.Equal([TextKeys.AskWhichMeaning], Keys(d)); // never a guessed price nor time
        Assert.Equal("حضرتك قصدك إن العقد بياخد وقت قد ايه، ولا إن العقد بيتكلف كام؟ 🙏", d.Actions[0].Text);
    }

    [Fact]
    public async Task One_meaning_or_none_is_no_reason_to_ask()
    {
        UrfFactsOn();
        _texts[TextKeys.AskWhichMeaning] = new ApprovedText(TextKeys.AskWhichMeaning, 1, "حضرتك قصدك {{meanings}}؟ 🙏", 300, true);
        await ToTheOffer();
        _script["الكلام هيفضل سري؟"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "privacy", Meanings: ["إن الموضوع يفضل سري"]);
        Assert.DoesNotContain(TextKeys.AskWhichMeaning, Keys(await Say("الكلام هيفضل سري؟")));
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

    private async Task<TurnDecision> UrfBookedAndPaying()
    {
        ApproveTask16Texts();
        UrfFactsOn();
        await ToTheGuardian(past: "first");
        await Answer(UrfStep.Guardian, "father");
        var times = await Answer(UrfStep.Witnesses, "office");
        await Press(times.Actions.Single().Interactive!.Choices[0].Id);
        _texts[TextKeys.ServiceCheck] = new ApprovedText(TextKeys.ServiceCheck, 1, "هسأل المكتب عن ده، ويردوا على حضرتك هنا.", 300, true);
        return times;
    }

    [Fact]
    public async Task A_change_to_a_booked_case_is_told_to_the_office_and_changes_nothing_by_itself()
    {
        await UrfBookedAndPaying();
        _script["هجيب الشهود من عندي"] = new("reschedule_or_cancel", null, "unknown", null, 0.75, UrfWitnesses: "own");
        var d = await Say("هجيب الشهود من عندي");
        Assert.Equal([TextKeys.ServiceCheck], Keys(d));
        Assert.Contains(PendingTask.UrfHelp, TaskKinds(d));
        Assert.Equal("office", TheCase.Witnesses); // the price paid and the documents are the office's decision
    }

    [Fact]
    public async Task Other_words_from_a_client_with_a_time_held_are_kept_for_the_office_not_answered_with_his_status()
    {
        await UrfBookedAndPaying();
        _script["هبعت المستندات لما اجي المكتب"] = new("other", null, "unknown", null, 0.7);
        var d = await Say("هبعت المستندات لما اجي المكتب");
        Assert.Equal([TextKeys.TopicReceived], Keys(d));
        Assert.Contains(PendingTask.ExistingClientMessage, TaskKinds(d));
    }

    [Fact]
    public async Task Where_to_pay_comes_after_the_answers_to_what_else_he_asked()
    {
        UnderstandingOn();
        OfficeOn();
        _settings[OfficeInfo.InstaPayKey] = "test@instapay";
        _settings[OfficeInfo.WalletNumberKey] = "01000000000";
        await ToTheOffer();
        _script["الحجز بيتم ازاي ومينفعش ادفع في المكتب"] = new("office_info", null, "unknown", null, 0.85, InfoTopic: "how_to_pay",
            AlsoAsked: ["how_to_book"]);
        var keys = Keys(await Say("الحجز بيتم ازاي ومينفعش ادفع في المكتب"));
        Assert.True(keys.IndexOf(TextKeys.OfficeHowToBook) < keys.IndexOf(TextKeys.OfficeHowToPay), string.Join(",", keys));
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
    public void While_collecting_what_he_says_fills_or_corrects_the_answers() // v1.74: a correction is the answer
    {
        var c = Marriage() with { HusbandAge = 30, WifeAge = 25, WifePast = "first", Step = UrfStep.Guardian };
        var said = UrfRules.ApplyFacts(c, "divorced", null, "office");
        Assert.Equal("divorced", said.WifePast); // «لا قصدي اتطلقت»: corrected
        Assert.Equal("office", said.Witnesses);
        Assert.Equal(UrfCase.DocumentsPending, said.State); // no guardian for a divorced wife, nothing left to ask: the price can go
    }

    [Fact] // v1.74 (Ahmed Amin's chat): «الأب», then «اقصد لا مفيش حد من اهل الزوجه»
    public void A_correction_to_no_guardian_refuses_a_first_marriage()
    {
        var c = Marriage() with { HusbandAge = 26, WifeAge = 27, WifePast = "first", Guardian = "father", Step = UrfStep.Witnesses };
        var said = UrfRules.ApplyFacts(c, null, "none", null);
        Assert.Equal((UrfCase.RejectedGuardian, "none"), (said.State, said.Guardian));
        Assert.Equal(UrfNext.RejectGuardian, UrfRules.NextOf(said));
    }

    [Fact] // v1.74 (Ahmed Amin's chat): «26و27», then «لا قصدي الزوجه 25»; and a correction under 18 refuses
    public void A_correction_of_the_ages_is_taken_and_checked()
    {
        var c = Marriage() with { HusbandAge = 26, WifeAge = 27, Step = UrfStep.WifePast };
        Assert.Equal((26, 25, UrfStep.WifePast), UrfRules.ApplyFacts(c, null, null, null, 26, 25) is var a ? (a.HusbandAge, a.WifeAge, a.Step) : default);
        Assert.Equal(UrfCase.RejectedAge, UrfRules.ApplyFacts(c, null, null, null, 26, 17).State);
        Assert.Equal(c, UrfRules.ApplyFacts(c, null, null, null, 26, null)); // one age alone changes nothing
    }

    [Fact]
    public void The_step_waiting_moves_past_what_was_said()
    {
        var c = Marriage(step: UrfStep.WifePast) with { HusbandAge = 30, WifeAge = 25 };
        Assert.Equal(UrfStep.Witnesses, UrfRules.ApplyFacts(c, "khul", null, null).Step); // v1.68: no guardian after a khul
        Assert.Equal(UrfStep.Guardian, UrfRules.ApplyFacts(c, "first", null, null).Step);
    }

    [Fact]
    public void The_ages_are_asked_first_and_no_guardian_refuses_once_they_are_in() // v1.74: «none» is a fact now
    {
        var said = UrfRules.ApplyFacts(Marriage(), "first", "none", "own");
        Assert.Equal((UrfCase.Collecting, UrfStep.Ages, "none"), (said.State, said.Step, said.Guardian)); // B-11's order: the ages first
        Assert.Equal((UrfCase.RejectedGuardian, UrfNext.RejectGuardian), UrfRules.Ages(said, "30 و 25") is var (c, n) ? (c.State, n) : default);
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
