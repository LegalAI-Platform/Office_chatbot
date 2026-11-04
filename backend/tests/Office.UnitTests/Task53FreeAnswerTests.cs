using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Understanding;
using Office.Application.Writing;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.53 (phases 2 and 3): with understanding v2 on, words the AI read that no approved text answers get the
/// writer's answer from the office facts and «دليل التعامل», checked by the code; the writer off or doubtful sends the
/// approved words; «دليل التعامل» reaches the writer only with the switch on, and is never sent.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string Playbook = "- السرية: كل كلام العميل مع المكتب سري.";

    private readonly FakeReviewer _reviewer = new();

    /// <summary>v1.53 (layer 4): the reviewer by meaning, as scripted (null: the reply may go).</summary>
    private sealed class FakeReviewer : IReplyReviewer
    {
        public string? Problem { get; set; }

        /// <summary>v1.56: the verdicts of the next reviews, in order (then <see cref="Problem"/>): a first try and its retry.</summary>
        public Queue<string?> Sequence { get; } = new();

        /// <summary>v1.58: what each review used, noted in the request as the real reviewer does.</summary>
        public Office.Application.Costs.TokenUsage? Tokens { get; set; }

        public ReviewRequest? Last { get; private set; }

        public Task<string?> ProblemAsync(ReviewRequest request, HazemSettings settings, CancellationToken ct)
        {
            Last = request;
            if (Tokens is { } t)
            {
                request.Usage?.Add(new Office.Application.Costs.AiUsage(Office.Application.Costs.AiUsageKind.Review, "fake-reviewer", t));
            }

            return Task.FromResult(Sequence.Count > 0 ? Sequence.Dequeue() : Problem);
        }
    }

    private void FreeAnswerOn()
    {
        UnderstandingOn();
        ConversationalOn();
        _texts[TextKeys.FreeAnswer] = new ApprovedText(TextKeys.FreeAnswer, 1, "معلش، ممكن توضّحلي أكتر حضرتك محتاج إيه؟ وأنا معاك 🙏", 300, true);
        _texts[TextKeys.OfficePlaybook] = new ApprovedText(TextKeys.OfficePlaybook, 1, Playbook, 600, true);
        _script["انا خايف مراتي تعرف"] = new("other", null, "unknown", null, 0.8);
    }

    [Fact]
    public async Task A_new_kind_of_moment_gets_the_writer_s_answer_from_the_facts_and_the_playbook()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Reply = "متقلقش يا فندم 🙏 كل كلام حضرتك مع المكتب سري، والمستشار هو اللي هيسمعك ويقولك تعمل إيه.";
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal([TextKeys.FreeAnswer], Keys(d));
        Assert.Equal(_writer.Reply, d.Actions[^1].Text);
        Assert.Equal(WrittenReply.Passed, d.Effects!.Written!.Guard);
        Assert.Contains(Playbook, _writer.Last!.Facts); // «دليل التعامل» is read by the writer, never sent
        Assert.DoesNotContain(d.Actions, a => a.Text?.Contains("دليل التعامل", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_promise_in_plain_words_is_caught_by_the_reviewer_and_the_approved_words_go()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Reply = "متقلقش يا فندم، مراتك مش هتعرف أبدًا 🙏"; // no listed word: only its meaning is a promise
        _reviewer.Problem = ReplyReview.Promise;
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal("معلش، ممكن توضّحلي أكتر حضرتك محتاج إيه؟ وأنا معاك 🙏", d.Actions[^1].Text);
        Assert.Equal("review_promise", d.Effects!.Written!.Guard);
        Assert.Equal(_writer.Reply, _reviewer.Last!.Reply);
        Assert.Contains(Playbook, _reviewer.Last.Facts);
    }

    [Fact]
    public async Task A_refused_reply_is_written_once_more_with_the_reason_and_the_second_one_goes()
    {
        FreeAnswerOn();
        await ToTheOffer();
        var before = _writer.All.Count; // the turns before it were written too
        _writer.Then.Enqueue("متقلقش يا فندم، مراتك مش هتعرف أبدًا 🙏");
        _writer.Then.Enqueue("متقلقش يا فندم 🙏 كلامك مع المكتب سري عند المكتب بس.");
        _reviewer.Sequence.Enqueue(ReplyReview.Promise); // the first is refused for its meaning
        _reviewer.Sequence.Enqueue(null);                // the second may go
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal("متقلقش يا فندم 🙏 كلامك مع المكتب سري عند المكتب بس.", d.Actions[^1].Text);
        Assert.Equal(WrittenReply.Passed, d.Effects!.Written!.Guard);
        Assert.EndsWith("+retry", d.Effects.Written.PromptVersion);
        Assert.Equal(before + 2, _writer.All.Count);
        Assert.Null(_writer.All[before].Feedback);
        Assert.Contains("مراتك مش هتعرف أبدًا", _writer.All[before + 1].Feedback); // it is told what was refused…
        Assert.Contains("promises", _writer.All[before + 1].Feedback);              // …and why
    }

    [Fact]
    public async Task A_reply_refused_twice_sends_the_approved_words_and_says_it_was_retried()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Reply = "متقلقش يا فندم، مراتك مش هتعرف أبدًا 🙏";
        _reviewer.Problem = ReplyReview.Promise;
        var before = _writer.Calls;
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal("معلش، ممكن توضّحلي أكتر حضرتك محتاج إيه؟ وأنا معاك 🙏", d.Actions[^1].Text);
        Assert.Equal("review_promise", d.Effects!.Written!.Guard);
        Assert.EndsWith("+retry", d.Effects.Written.PromptVersion);
        Assert.Equal(before + 2, _writer.Calls); // once more, never a third time
    }

    [Fact]
    public async Task A_reviewer_that_could_not_answer_is_not_retried_and_with_the_switch_off_nothing_is_retried()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Reply = "متقلقش يا فندم 🙏";
        _reviewer.Problem = ReplyReview.Unavailable + "_timeout";
        var first = _writer.Calls;
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal("review_unavailable_timeout", d.Effects!.Written!.Guard);
        Assert.Equal(first + 1, _writer.Calls); // infrastructure is no reason to write again

        _settings[UnderstandingV2.SwitchKey] = "false";
        _reviewer.Problem = null;
        _writer.Reply = "متقلقش، القانون بيحميك والعقوبة حبس سنة."; // refused by the word check
        var calls = _writer.Calls;
        await Say("بجد خايف");
        Assert.Equal(calls + 1, _writer.Calls); // one call: as before
    }

    [Fact]
    public async Task Every_active_part_of_the_playbook_reaches_the_writer_and_the_reviewer_in_order()
    {
        FreeAnswerOn();
        _texts[TextKeys.OfficePlaybookMore] = new ApprovedText(TextKeys.OfficePlaybookMore, 1, "- الجزء التاني.", 600, true);
        _texts[TextKeys.OfficePlaybookMore2] = new ApprovedText(TextKeys.OfficePlaybookMore2, 1, "- الجزء التالت: السرية بثقة.", 600, true);
        await ToTheOffer();
        _writer.Reply = "متقلقش يا فندم 🙏 كلامك مع المكتب سري.";
        await Say("انا خايف مراتي تعرف");
        var facts = _writer.Last!.Facts;
        Assert.True(facts.IndexOf(Playbook, StringComparison.Ordinal) < facts.IndexOf("- الجزء التاني.", StringComparison.Ordinal));
        Assert.True(facts.IndexOf("- الجزء التاني.", StringComparison.Ordinal) < facts.IndexOf("- الجزء التالت", StringComparison.Ordinal));
        Assert.Contains("- الجزء التالت", _reviewer.Last!.Facts); // the reviewer checks the reply against the same rules
    }

    [Fact]
    public async Task A_reply_that_breaks_a_rule_of_the_playbook_is_written_again_with_that_reason()
    {
        FreeAnswerOn();
        await ToTheOffer();
        var before = _writer.All.Count;
        _writer.Then.Enqueue("بس مش أقدر أضمنلك إن حد مش هيعرف حاجة 🙏");
        _writer.Then.Enqueue("أيوه، كلامك مع المكتب سري.");
        _reviewer.Sequence.Enqueue(ReplyReview.BreaksPlaybook);
        _reviewer.Sequence.Enqueue(null);
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal("أيوه، كلامك مع المكتب سري.", d.Actions[^1].Text);
        Assert.Contains("rule of «دليل التعامل»", _writer.All[before + 1].Feedback);
    }

    [Fact]
    public async Task With_the_switch_off_the_reviewer_is_never_called()
    {
        await InSituation("new");
        ConversationalOn();
        _writer.Reply = "أهلًا بحضرتك 👋 أقدر أساعدك في إيه النهارده؟";
        await Say("السلام عليكم");
        Assert.Null(_reviewer.Last);
    }

    [Fact]
    public async Task With_the_switch_on_every_written_reply_is_reviewed_and_a_promise_in_a_comfort_reply_never_goes()
    {
        // the test server's first try: «ومحدش هيعرف حاجة» came out of a comfort reply that was no free answer
        await InSituation("new");
        ConversationalOn();
        UnderstandingOn();
        _writer.Reply = "أهلًا بحضرتك 👋 أقدر أساعدك في إيه النهارده؟";
        var ok = await Say("السلام عليكم");
        Assert.NotNull(_reviewer.Last); // reviewed, though it is no free answer
        Assert.Equal(_writer.Reply, ok.Actions[^1].Text);
        Assert.Contains("النص المعتمد", _reviewer.Last!.Facts); // the approved text it rewrites counts as given

        _writer.Reply = "أهلًا بحضرتك 👋 وطمّن حضرتك إن محدش هيعرف حاجة.";
        _reviewer.Problem = ReplyReview.Promise;
        var d = await Say("السلام عليكم");
        Assert.NotEqual(_writer.Reply, d.Actions[^1].Text); // the approved words went
        Assert.Equal("review_promise", d.Effects!.Written!.Guard);
    }

    [Fact]
    public async Task A_doubtful_free_answer_sends_the_approved_words()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Reply = "متقلقش، القانون بيحميك والعقوبة حبس سنة."; // legal words and a number not in the facts
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal([TextKeys.FreeAnswer], Keys(d));
        Assert.Equal("معلش، ممكن توضّحلي أكتر حضرتك محتاج إيه؟ وأنا معاك 🙏", d.Actions[^1].Text);
        Assert.NotEqual(WrittenReply.Passed, d.Effects!.Written!.Guard);
    }

    [Fact]
    public async Task Free_answers_never_hand_the_chat_to_the_office_while_the_AI_reads_the_words()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Reply = "متقلقش يا فندم 🙏 كل كلام حضرتك مع المكتب سري.";
        await Say("انا خايف مراتي تعرف");
        _writer.Reply = "ولا يهمك 🙏 المستشار هو اللي هيسمعك.";
        _script["بجد خايف"] = new("other", null, "unknown", null, 0.8);
        Assert.Equal([TextKeys.FreeAnswer], Keys(await Say("بجد خايف"))); // not «هبلّغ المكتب»
    }

    [Fact]
    public async Task With_the_switch_off_the_playbook_never_reaches_the_writer_and_unclear_is_as_before()
    {
        FreeAnswerOn();
        _settings[UnderstandingV2.SwitchKey] = "false";
        await ToTheOffer();
        var d = await Say("انا خايف مراتي تعرف");
        Assert.DoesNotContain(TextKeys.FreeAnswer, Keys(d));
        Assert.DoesNotContain(Playbook, _writer.Last?.Facts ?? "");
    }
}

/// <summary>v1.53 (B-34, layer 4): the reviewer's answer read strictly: anything but the schema fails closed.</summary>
public sealed class ReplyReviewVerdictTests
{
    [Theory]
    [InlineData("""{"ok": true, "problem": "none"}""", null)]
    [InlineData("""{"ok": false, "problem": "promise"}""", "promise")]
    [InlineData("""{"ok": false, "problem": "legal"}""", "legal")]
    [InlineData("""{"ok": false, "problem": "breaks_playbook"}""", "breaks_playbook")]
    [InlineData("""{"ok": true, "problem": "harmful"}""", "harmful")] // a problem named is a problem
    [InlineData("""{"ok": false, "problem": "other"}""", "unavailable_invalid")]
    [InlineData("not json", "unavailable_invalid")]
    [InlineData(null, "unavailable_invalid")]
    public void The_verdict_is_read_strictly(string? content, string? expected) =>
        Assert.Equal(expected, Office.Infrastructure.Ai.AnthropicReplyReviewer.Verdict(content));
}
