using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Writing;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.111 (Ahmed Amin's chat, 2026-11-14 04:23: «صياغة العقد بتبدأ من 3,000 جنيه حسب نوعه. ابعتلي أي تفاصيل أو ورق
/// عندك.» went four times in a row, under «عقد ايجار», under «✅ ابدأ الطلب» and after the order opened; the owner: «General
/// fix»): the forbidden rule 2 watches every everyday text, the AI's reading leads when the same answer comes again, «✅ ابدأ
/// الطلب» says the request is open, and an open order is never started again.
/// </summary>
public sealed class V111NoRepeatRulesTests
{
    [Theory]
    [InlineData(TextKeys.DocumentReviewRequest)]
    [InlineData(TextKeys.ContractDraftingRequest)]
    [InlineData(TextKeys.FormalNoticeRequest)]
    public void A_written_service_s_text_is_said_in_the_writer_s_words(string key) =>
        Assert.True(Conversational.MayWrite(key));

    [Fact]
    public void The_new_text_is_never_one_the_writer_may_say_in_its_words() =>
        Assert.False(Conversational.MayWrite(TextKeys.ServiceOrderOpened)); // a request opened is said as approved

    [Fact]
    public void A_reply_holding_the_last_message_whole_is_the_same_message()
    {
        const string before = "صياغة العقد بتبدأ من 3,000 جنيه حسب نوعه. ابعتلي أي تفاصيل أو ورق عندك.";
        Assert.True(ReplyGuard.HoldsWhole("تمام 👍 " + before, before));
        Assert.True(ReplyGuard.HoldsWhole(before.Replace(" ", "  ", StringComparison.Ordinal), before)); // spaces aside
        Assert.False(ReplyGuard.HoldsWhole("تمام، عقد إيجار 👍 ابعتلي تفاصيل الشقة وأطراف العقد، أو أي ورق عندك.", before));
    }

    [Theory]
    [InlineData(TextKeys.AskCallApp, true)]
    [InlineData(TextKeys.BookingChooseSlot, true)]
    [InlineData(TextKeys.ContractDraftingRequest, false)]
    public void A_question_of_choices_asked_again_is_the_flow_s_own(string key, bool choice) =>
        Assert.Equal(choice, Conversational.IsChoiceQuestion(key));
}

public sealed partial class BookingFlowTests
{
    private const string DraftingText = "صياغة العقد بتبدأ من 3,000 جنيه حسب نوعه. ابعتلي أي تفاصيل أو ورق عندك.";
    private const string OpenedText = "تمام، طلب {{service}} اتفتح ✅ ابعتلي هنا أي تفاصيل أو ورق عندك، والمستشار هيحدد السعر النهائي قبل أي دفع.";

    /// <summary>Ahmed Amin's first two messages: «السلام عليكم» (the welcome), then the contract (its text and «✅ ابدأ الطلب»).</summary>
    private async Task<TurnDecision> AhmedAsksForAContract(bool writer = false)
    {
        await InSituation("new");
        ServicesOn();
        if (writer)
        {
            ConversationalOn();
        }

        _texts[TextKeys.BtnStartOrder] = new ApprovedText(TextKeys.BtnStartOrder, 1, "✅ ابدأ الطلب", 20, true);
        _texts[TextKeys.ContractDraftingRequest] = new ApprovedText(TextKeys.ContractDraftingRequest, 1, DraftingText, 300, true);
        _texts[TextKeys.ServiceOrderOpened] = new ApprovedText(TextKeys.ServiceOrderOpened, 1, OpenedText, 300, true);
        _texts[TextKeys.ServiceDetail] = new ApprovedText(TextKeys.ServiceDetail, 1, "سؤال حضرتك وصل للمكتب، وهيردوا عليك هنا.", 300, true);
        _script["عايز اكتب عقد هل ده موجود في المكتب؟"] = new("service_by_name", "contract_drafting", "unknown", null, 0.95);
        _script["عقد ايجار هكتب عقد ايجار"] = new("service_by_name", "contract_drafting", "unknown", null, 0.95);
        _script["ورق زي ايه مش معايا ورق انا"] = new("office_info", "contract_drafting", "unknown", null, 0.95, InfoTopic: "service_detail");
        _writer.Reply = "أيوه 👍 صياغة العقد بتبدأ من 3,000 جنيه حسب نوعه، ابعتلي أي تفاصيل أو ورق عندك.";
        var asked = await Say("عايز اكتب عقد هل ده موجود في المكتب؟");
        Assert.Equal([TextKeys.ContractDraftingRequest], Keys(asked));
        Assert.Equal([ServiceRow.Id(ServiceRow.Drafting)], Assert.Single(asked.Actions).Interactive!.Choices.Select(c => c.Id));
        Assert.Empty(_booking.Orders); // v1.43: asking opens nothing
        return asked;
    }

    [Fact]
    public async Task V111_the_drafting_text_answered_with_a_detail_is_never_sent_again_word_for_word()
    {
        await AhmedAsksForAContract();
        var again = await Say("عقد ايجار هكتب عقد ايجار");
        Assert.Equal([TextKeys.RepeatChoice], Keys(again)); // the writer off: v1.39's way out, never the same words
        Assert.DoesNotContain(again.Actions, a => a.Text == DraftingText);
        Assert.Empty(_booking.Orders);
    }

    [Fact]
    public async Task V111_with_the_writer_the_same_step_is_said_anew_answering_his_detail_under_the_same_button()
    {
        var first = await AhmedAsksForAContract(writer: true);
        Assert.Equal("أيوه 👍 صياغة العقد بتبدأ من 3,000 جنيه حسب نوعه، ابعتلي أي تفاصيل أو ورق عندك.", first.Actions[0].Text); // its price kept

        _writer.Reply = "تمام، عقد إيجار 👍 ابعتلي تفاصيل الشقة وأطراف العقد، أو أي ورق عندك."; // no price: he got it last time
        var again = await Say("عقد ايجار هكتب عقد ايجار");
        var a = Assert.Single(again.Actions);
        Assert.Equal(_writer.Reply, a.Text);
        Assert.Equal([ServiceRow.Id(ServiceRow.Drafting)], a.Interactive!.Choices.Select(c => c.Id)); // still «✅ ابدأ الطلب»
        Assert.Equal([TextKeys.ContractDraftingRequest], Keys(again));
        Assert.Equal(WrittenReply.Passed, again.Effects!.Written!.Guard);
        Assert.StartsWith("he got this very answer from you in your last message", _writer.Last!.Purpose, StringComparison.Ordinal);
        Assert.Contains("drafting a contract", _writer.Last.Purpose, StringComparison.Ordinal); // and the same next step
    }

    [Fact]
    public async Task V111_the_writer_down_when_the_same_answer_comes_again_sends_the_way_out_not_the_same_words()
    {
        await AhmedAsksForAContract(writer: true);
        _writer.Reply = null; // down
        var again = await Say("عقد ايجار هكتب عقد ايجار");
        Assert.Equal([TextKeys.RepeatChoice], Keys(again));
        Assert.Equal([ServiceRow.Id(ServiceRow.Menu), ServiceRow.Id(ServiceRow.Human)], Assert.Single(again.Actions).Interactive!.Choices.Select(c => c.Id));
        Assert.Contains(again.Notes!, n => n.StartsWith("repeat not written anew", StringComparison.Ordinal));
    }

    [Fact]
    public async Task V111_start_pressed_under_the_text_says_the_request_is_open_and_opens_it()
    {
        await AhmedAsksForAContract();
        var started = await Press(ServiceRow.Id(ServiceRow.Drafting)); // «✅ ابدأ الطلب»
        Assert.Equal([TextKeys.ServiceOrderOpened], Keys(started));
        Assert.Equal("تمام، طلب صياغة العقد اتفتح ✅ ابعتلي هنا أي تفاصيل أو ورق عندك، والمستشار هيحدد السعر النهائي قبل أي دفع.",
            Assert.Single(started.Actions).Text);
        Assert.Equal(("contract_drafting", "collecting"), (Assert.Single(_booking.Orders).Kind, _booking.Orders[0].State));
        Assert.Contains(PendingTask.ServiceRequest, TaskKinds(started));
    }

    [Fact]
    public async Task V111_start_pressed_after_the_way_out_still_says_the_request_is_open()
    {
        await AhmedAsksForAContract();
        Assert.Equal([TextKeys.RepeatChoice], Keys(await Say("عقد ايجار هكتب عقد ايجار")));
        // the button under the text two messages back: he read it in his last answers
        Assert.Equal([TextKeys.ServiceOrderOpened], Keys(await Press(ServiceRow.Id(ServiceRow.Drafting))));
    }

    [Fact]
    public async Task V111_once_open_its_question_is_answered_and_nothing_is_started_again()
    {
        await AhmedAsksForAContract();
        await Press(ServiceRow.Id(ServiceRow.Drafting));

        var asked = await Say("ورق زي ايه مش معايا ورق انا");
        Assert.Equal([TextKeys.ServiceDetail], Keys(asked)); // its answer only
        Assert.DoesNotContain(asked.Actions, a => a.Interactive is not null); // never «✅ ابدأ الطلب» again

        // v1.112: naming it again is his words about his open request: they reach the office, never its status in their place
        var named = await Say("عايز اكتب عقد هل ده موجود في المكتب؟");
        Assert.Equal([TextKeys.ServiceDetail], Keys(named));
        Assert.Contains(PendingTask.ServiceQuestion, TaskKinds(named));
        Assert.DoesNotContain(named.Actions, a => a.Text?.Contains("مستني الورق", StringComparison.Ordinal) == true);
        Assert.Single(_booking.Orders);

        var again = await Press(ServiceRow.Id(ServiceRow.Drafting)); // the old button once more: open, nothing new
        Assert.Equal([TextKeys.ServiceOrderOpened], Keys(again));
        Assert.Single(_booking.Orders);
    }

    [Fact]
    public async Task V111_two_questions_in_a_row_each_reach_the_office_never_the_way_out()
    {
        await AhmedAsksForAContract();
        await Press(ServiceRow.Id(ServiceRow.Drafting));
        _script["بياخد قد ايه؟"] = new("office_info", "contract_drafting", "unknown", null, 0.95, InfoTopic: "service_detail");
        Assert.Equal([TextKeys.ServiceDetail], Keys(await Say("ورق زي ايه مش معايا ورق انا")));
        var second = await Say("بياخد قد ايه؟"); // the same text, but his new question reached the office (a task)
        Assert.Equal([TextKeys.ServiceDetail], Keys(second));
        Assert.Contains(PendingTask.ServiceQuestion, TaskKinds(second));
    }

    [Fact]
    public async Task V111_ahmed_amin_s_chat_replayed_never_says_the_same_message_twice_running()
    {
        var sent = new List<string?> { (await AhmedAsksForAContract()).Actions[^1].Text };
        foreach (var next in new Func<Task<TurnDecision>>[]
                 {
                     () => Say("عقد ايجار هكتب عقد ايجار"),
                     () => Press(ServiceRow.Id(ServiceRow.Drafting)),
                     () => Say("ورق زي ايه مش معايا ورق انا"),
                 })
        {
            var d = await next();
            Assert.DoesNotContain(d.Actions, a => a.Text == sent[^1]); // never word for word what he just got
            sent.Add(d.Actions[^1].Text);
        }

        Assert.Equal(1, sent.Count(t => t == DraftingText)); // the drafting text: once
    }
}
