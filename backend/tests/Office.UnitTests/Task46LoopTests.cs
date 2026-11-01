using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.46: the test chat of 2026-11-01 («لو هتجوز عرفي هل في خصوصية» → «سن الزوج كام…» twice). A question about
/// how a service goes reaches the office and the path goes on; a Urfi question is never sent the same way twice in a row
/// (the second time «علشان نكمّل…», the third the office); a legal question in the middle keeps the Urfi path.
/// </summary>
public sealed partial class BookingFlowTests
{
    private void ApproveNoLoopTexts()
    {
        UrfOn();
        foreach (var (k, v) in new Dictionary<string, string>
                 {
                     [TextKeys.ServiceDetail] = "سؤال مهم 👌 هوصّله للمكتب، ويردوا على حضرتك هنا.",
                     [TextKeys.UrfStepAgain] = "علشان نكمّل طلب حضرتك، محتاج إجابة السؤال ده 👇",
                     [TextKeys.ServiceCheck] = "هسأل المكتب عن ده، ويردوا على حضرتك هنا.",
                     [TextKeys.UnclearAgain] ="تمام، هبلّغ المكتب برسالة حضرتك.",
                     [TextKeys.UrfQuestionLater] = "المستشار هيوضح النقطة دي لحضرتك في الميعاد.",
                 })
        {
            _texts[k] = new ApprovedText(k, 1, v, 300, true);
        }

        _script["لو هتجوز عرفي هل في خصوصية ولا لا"] = new("office_info", "urf_marriage", "unknown", null, 0.8, "urfi", "urf_marriage",
            InfoTopic: "service_detail");
        _script["لا انا بسئل بس هل في خصوصية ولا لا"] = new("service_by_name", "urf_marriage", "unknown", null, 0.6, "urfi", "urf_marriage");
    }

    [Fact]
    public async Task The_test_chat_a_question_about_the_Urfi_contract_reaches_the_office_and_the_ages_are_never_asked_the_same_way_twice()
    {
        ApproveNoLoopTexts();
        await ToTheOffer();

        var asked = await Say("لو هتجوز عرفي هل في خصوصية ولا لا");
        Assert.Equal([TextKeys.ServiceDetail, TextKeys.UrfAskAges], Keys(asked)); // the office answers it; the path starts
        Assert.Contains(PendingTask.ServiceQuestion, TaskKinds(asked));

        Assert.Equal([TextKeys.UrfAgesAgain], Keys(await Say("لا انا بسئل بس هل في خصوصية ولا لا"))); // not «سن الزوج كام» again

        var third = await Say("لا انا بسئل بس هل في خصوصية ولا لا");
        Assert.Equal([TextKeys.UnclearAgain], Keys(third)); // the office takes it
        Assert.Contains(PendingTask.UrfHelp, TaskKinds(third));
    }

    [Fact]
    public async Task A_question_about_the_service_while_the_ages_wait_reaches_the_office_and_the_ages_come_back_in_other_words()
    {
        ApproveNoLoopTexts();
        await ToTheOffer();
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say("عايز أعمل جواز عرفي")));

        var d = await Say("لو هتجوز عرفي هل في خصوصية ولا لا");
        Assert.Equal([TextKeys.ServiceDetail, TextKeys.UrfAgesAgain], Keys(d));
        Assert.Contains(PendingTask.ServiceQuestion, TaskKinds(d));
        Assert.Equal(UrfStep.Ages, TheCase.Step);
    }

    [Fact]
    public async Task A_Urfi_question_with_buttons_left_unanswered_comes_back_once_with_its_words_then_the_office_takes_it()
    {
        ApproveNoLoopTexts();
        await ToTheGuardian();
        _script["مش فاهم"] = new("other", null, "unknown", null, 0.9);

        var again = await Say("مش فاهم");
        Assert.Equal([TextKeys.UrfStepAgain, TextKeys.UrfAskGuardian], Keys(again)); // its list still under it
        Assert.Contains(again.Actions, a => a.Type == ActionType.SendInteractive);

        var third = await Say("مش فاهم");
        Assert.Equal([TextKeys.UnclearAgain], Keys(third));
        Assert.Contains(PendingTask.UrfHelp, TaskKinds(third));
    }

    [Fact]
    public async Task A_legal_question_in_the_middle_of_the_Urfi_questions_keeps_the_path_without_the_consultation_offer()
    {
        ApproveNoLoopTexts();
        await ToTheGuardian();
        _script["هل الشهود هيتكتمو على الزواج"] = new("legal_question", null, "unknown", null, 0.9, "urfi", "urf_marriage");

        var d = await Say("هل الشهود هيتكتمو على الزواج");
        Assert.Equal([TextKeys.UrfQuestionLater, TextKeys.UrfStepAgain, TextKeys.UrfAskGuardian], Keys(d));
        Assert.DoesNotContain(TextKeys.ConsultationOffer, Keys(d));
    }

    [Fact]
    public async Task Without_its_text_approved_a_question_about_a_service_gets_the_office_s_answer_as_before()
    {
        ApproveNoLoopTexts();
        _texts[TextKeys.ServiceDetail] = _texts[TextKeys.ServiceDetail] with { Active = false };
        await ToTheOffer();

        var d = await Say("لو هتجوز عرفي هل في خصوصية ولا لا");
        Assert.Equal([TextKeys.ServiceCheck, TextKeys.UrfAskAges], Keys(d));
    }
}
