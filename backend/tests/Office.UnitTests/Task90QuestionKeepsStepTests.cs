using Office.Application.Booking;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;
using Office.Application.Conversations;
using Office.Domain.Tasks;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.90 (Ahmed Amin's chat, 2026-11-07, switch on): «طب هو لازم يكون في شهود؟ ولا ممكن نعمل العقد بينا إحنا الاتنين بس؟»
/// asked three times and never answered. His question is never a wrong answer: (A) the code reads the ages alone only when the
/// message IS the ages; (B) «without witnesses» is asked like any answer and answered by the office's rule; (C) a question nothing
/// answers gets «المستشار هيوضح…» and the question waiting, never «again» and never the office; the same for the booking's times
/// and for the name or number a picked time waits for. Switch off: as v1.81.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string WitnessWords = "طب هو لازم يكون في شهود؟ ولا ممكن نعمل العقد بينا إحنا الاتنين بس؟";

    private void WitnessesNeededOn()
    {
        _texts[TextKeys.UrfQuestionLater] = new ApprovedText(TextKeys.UrfQuestionLater, 1, "المستشار هيوضح النقطة دي لحضرتك في الميعاد.", 300, true);
        _texts[TextKeys.UrfWitnessesNeeded] = new ApprovedText(TextKeys.UrfWitnessesNeeded, 1,
            "أيوه، العقد عندنا لازم يكون فيه شاهدين: المكتب يوفّرهم، أو حضرتك تجيبهم.", 300, true);
    }

    /// <summary>Switch on, the texts approved, the Urfi marriage asked: the ages question waits.</summary>
    private async Task ToTheAges()
    {
        CaseAnswersOn();
        WitnessesNeededOn();
        await ToTheOffer();
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say("عايز أعمل جواز عرفي")));
    }

    [Theory]
    [InlineData("30 و 25", true)]
    [InlineData("الزوج 30 والزوجة 25", true)]
    [InlineData("انا 30 سنه وهي 25 سنه", true)]
    [InlineData("الاتنين 25", true)]
    [InlineData("77و90 طب هو لازم يكون في شهود؟", false)]
    [InlineData("30 و 25 والعقد بياخد وقت قد ايه", false)]
    [InlineData("لو هي 17 سنة ينفع؟", false)] // not two ages
    public void The_ages_are_the_whole_message_only_when_nothing_else_is_said(string text, bool only) =>
        Assert.Equal(only, UrfAges.IsOnly(text));

    [Fact]
    public async Task Ahmed_Amins_chat_the_question_without_witnesses_is_answered_under_the_ages()
    {
        await ToTheAges();
        _script[WitnessWords] = new("service_by_name", "urf_marriage", "unknown", null, 0.8,
            CaseSaid: [Asked(UrfStep.Witnesses, "none")], Asks: true);
        Assert.Equal([TextKeys.UrfWitnessesNeeded, TextKeys.UrfAskAges], Keys(await Say(WitnessWords))); // never «بالأرقام»
        Assert.Equal((UrfCase.Collecting, UrfStep.Ages), (TheCase.State, TheCase.Step));
    }

    [Fact]
    public async Task Ahmed_Amins_chat_the_ages_with_the_question_take_the_ages_and_answer_it()
    {
        await ToTheAges();
        const string words = "77و90 طب هو لازم يكون في شهود؟";
        _script[words] = new("legal_question", "urf_marriage", "unknown", null, 0.8,
            CaseSaid: [new UrfSaid(UrfStep.Ages, UrfKind.Said, null, 77, 90), Asked(UrfStep.Witnesses, "none")], Asks: true);
        Assert.Equal([TextKeys.UrfWitnessesNeeded, TextKeys.UrfAskWifePast], Keys(await Say(words)));
        Assert.Equal((77, 90, UrfStep.WifePast), (TheCase.HusbandAge, TheCase.WifeAge, TheCase.Step));
        Assert.Null(TheCase.Witnesses); // a question changes nothing
    }

    [Fact]
    public async Task The_ages_with_more_words_are_still_taken_when_the_AI_cannot_read_them()
    {
        await ToTheAges();
        // no script: the classifier falls back
        Assert.Equal([TextKeys.UrfAskWifePast], Keys(await Say("30 و 25 تمام كده")));
        Assert.Equal((30, 25), (TheCase.HusbandAge, TheCase.WifeAge));
    }

    [Fact]
    public async Task Witnesses_none_said_is_never_an_answer()
    {
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        _script["مفيش شهود"] = new("other", "urf_marriage", "unknown", null, 0.8, CaseSaid: [SaidItem(UrfStep.Witnesses, "none")]);
        await Say("مفيش شهود");
        Assert.Null(TheCase.Witnesses);
    }

    [Fact]
    public async Task A_question_nothing_answers_gets_later_and_the_question_never_again_nor_the_office()
    {
        await ToTheAges();
        const string q = "طب والعقد ده بيتسجل فين؟";
        _script[q] = new("service_by_name", "urf_marriage", "unknown", null, 0.8, Asks: true);
        Assert.Equal([TextKeys.UrfQuestionLater, TextKeys.UrfAskAges], Keys(await Say(q)));
        var second = await Say(q);
        Assert.Equal([TextKeys.UrfQuestionLater, TextKeys.UrfAskAges], Keys(second)); // a second question is not the office's
        Assert.DoesNotContain(PendingTask.UrfHelp, TaskKinds(second));
    }

    [Fact]
    public async Task Words_that_ask_nothing_still_get_again_then_the_office_as_v1_46()
    {
        await ToTheAges();
        _script["تمام"] = new("other", "urf_marriage", "unknown", null, 0.8, Asks: false);
        Assert.Equal([TextKeys.UrfAgesAgain], Keys(await Say("تمام")));
    }

    [Fact]
    public async Task With_the_switch_off_Ahmed_Amins_question_is_as_v1_81()
    {
        UrfFactsOn(); // understanding v2 on, hazem.urf_case_said off
        WitnessesNeededOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        _script[WitnessWords] = new("service_by_name", "urf_marriage", "unknown", null, 0.8,
            CaseSaid: [Asked(UrfStep.Witnesses, "none")], Asks: true);
        Assert.Equal([TextKeys.UrfAgesAgain], Keys(await Say(WitnessWords)));
        Assert.Equal([TextKeys.UrfAskWifePast], Keys(await Say("77و90 طب هو لازم يكون في شهود؟"))); // the code reads the ages
    }

    // ---------- the booking: an answered question under the times is not a repeat ----------

    [Fact]
    public async Task A_question_answered_under_the_times_brings_the_times_back_as_they_are_twice()
    {
        CaseSaidOn();
        await ToTheOfficeTimes();
        const string q = "هو الأستاذ هيقدر يرفع قضية نفقة؟";
        _script[q] = new("legal_question", null, "unknown", null, 0.9, Asks: true);
        Assert.Equal([TextKeys.LegalQuestion, TextKeys.BookingChooseSlot], Keys(await Say(q)));
        var second = await Say(q);
        Assert.Equal([TextKeys.LegalQuestion, TextKeys.BookingChooseSlot], Keys(second)); // never «علشان نكمّل الحجز», never the office
        Assert.DoesNotContain(PendingTask.UnclearMessage, TaskKinds(second));
    }

    [Fact]
    public async Task With_the_switch_off_the_question_under_the_times_is_as_v1_81()
    {
        await ToTheOfficeTimes();
        const string q = "هو الأستاذ هيقدر يرفع قضية نفقة؟";
        _script[q] = new("legal_question", null, "unknown", null, 0.9, Asks: true);
        Assert.Equal([TextKeys.LegalQuestion, TextKeys.BookingStepAgain, TextKeys.BookingChooseSlot], Keys(await Say(q)));
        Assert.Contains(TextKeys.UnclearAgain, Keys(await Say(q))); // the second question: the office, as v1.47
    }

    // ---------- the name a picked time waits for ----------

    [Fact]
    public async Task A_question_instead_of_the_name_is_answered_and_the_time_keeps_waiting()
    {
        CaseSaidOn();
        await ToTheOfferWithoutName();
        await Press(NamePick);
        _script["هدفع ازاي؟"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "how_to_pay", Asks: true);
        var d = await Say("هدفع ازاي؟");
        Assert.Equal(TextKeys.AskNameBooking, Keys(d)[^1]); // the name again, after the answer
        Assert.DoesNotContain(TextKeys.AskNameAgain, Keys(d));
        Assert.Equal(NamePick, _rows[Number].Intake.PickWaitingForName); // the time still waits

        _script["أحمد محمود"] = new("provide_name", null, "unknown", "أحمد محمود", 0.95);
        Assert.Equal([TextKeys.BookingHold], Keys(await Say("أحمد محمود")));
        Assert.Equal(NameSlot, Assert.Single(_booking.Appointments).Start);
    }

    [Fact]
    public async Task With_the_switch_off_a_question_instead_of_the_name_lets_the_time_go_as_v1_81()
    {
        await ToTheOfferWithoutName();
        await Press(NamePick);
        _script["هدفع ازاي؟"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "how_to_pay", Asks: true);
        Assert.DoesNotContain(TextKeys.AskNameBooking, Keys(await Say("هدفع ازاي؟")));
        Assert.Null(_rows[Number].Intake.PickWaitingForName);
    }
}
