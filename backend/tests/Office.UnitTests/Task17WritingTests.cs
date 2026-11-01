using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Writing;

namespace Office.UnitTests;

/// <summary>
/// TASK 17 (DECISIONS v1.41): the everyday replies in the writer's words. The code decides what is said (the approved text and
/// its facts); the writer says it while it sees the conversation; the code checks every sentence (ReplyGuard) and anything
/// doubtful, a writer down or the switch off sends the approved text exactly as before.
/// </summary>
public sealed class ReplyGuardTests
{
    private static WriteRequest R(string approved) => new("purpose", approved, "", []);

    [Theory]
    [InlineData("فاهم إن الموضوع مقلقك. المستشار هيكلم حضرتك حوالي الساعة 10:00 ص.", null)]
    [InlineData("المستشار هيكلم حضرتك حوالي الساعة ١٠:٠٠ ص.", null)] // Arabic-Indic digits are the same facts
    [InlineData("المستشار هيكلم حضرتك الساعة 11:00 ص.", "number_not_given")]
    [InlineData("الاستشارة بـ500 جنيه بس.", "number_not_given")]
    [InlineData("متقلقش، النفقة عقوبتها حبس شهر بس.", "legal_word")]
    [InlineData("متقلقش، من حقك ترفض.", "legal_word")]
    [InlineData("متقلقش، مضمون المستشار هيحلها.", "promise")]
    [InlineData("المستشار هيكلمك حالًا.", "promise")]
    [InlineData("ابعت الفلوس على المحفظة.", "money")]
    [InlineData("شوف www.example.com", "link")]
    [InlineData("Don't worry, the lawyer will call you.", "not_arabic")]
    [InlineData("   ", "empty")]
    public void The_check_lets_through_only_the_approved_facts_and_no_law_promise_or_money(string reply, string? problem) =>
        Assert.Equal(problem, ReplyGuard.Problem(reply, R("أكيد 👋 حضرتك عندك استشارة عاجلة مدفوعة، والمستشار هيكلم حضرتك حوالي الساعة 10:00 ص."), null));

    [Fact]
    public void A_sentence_too_long_or_the_same_as_the_last_one_does_not_go()
    {
        Assert.Equal("too_long", ReplyGuard.Problem(new string('ا', 301), R("تمام"), null));
        Assert.Equal("same_as_previous", ReplyGuard.Problem("تمام، وصلت ✅", R("تمام، وصلت ✅"), "تمام،  وصلت ✅"));
    }

    [Fact]
    public void A_word_of_the_approved_text_itself_is_allowed()
    {
        // The held time's text speaks of «صورة التحويل»: the writer may too.
        Assert.Null(ReplyGuard.Problem("ميعادك ممسوك ومستني صورة التحويل بس يا فندم ✅", R("ميعاد حضرتك ممسوك، ومستني صورة التحويل بس."), null));
    }

    [Theory]
    [InlineData(TextKeys.UrgentPay)]
    [InlineData(TextKeys.BookingHold)]
    [InlineData(TextKeys.BookingPaid)]
    [InlineData(TextKeys.ServicesMenu)]
    [InlineData(TextKeys.LegalQuestion)]
    [InlineData(TextKeys.ConsultationOffer)]
    [InlineData(TextKeys.AbuseWarning)]
    public void Where_to_pay_confirmations_prices_and_the_legal_refusal_always_stay_approved(string key) =>
        Assert.False(Conversational.MayWrite(key));
}

public sealed partial class BookingFlowTests
{
    private readonly FakeWriter _writer = new();

    private void ConversationalOn()
    {
        _settings[Conversational.SwitchKey] = "true";
        _texts[TextKeys.GreetSalam] = new ApprovedText(TextKeys.GreetSalam, 1, "وعليكم السلام ورحمة الله وبركاته", 300, true);
    }

    [Fact]
    public async Task V141_switch_off_the_approved_text_goes_and_the_writer_is_never_called()
    {
        ApproveTask16Texts();
        await Say("السلام عليكم");
        var d = await Say("السلام عليكم");
        Assert.Equal(0, _writer.Calls);
        Assert.Null(d.Effects!.Written);
    }

    [Fact]
    public async Task V141_switch_on_the_greeting_is_said_in_the_writer_s_words_and_its_key_is_kept()
    {
        await InSituation("new");
        ConversationalOn();
        _writer.Reply = "أهلًا بحضرتك 👋 أقدر أساعدك في إيه النهارده؟";
        var d = await Say("السلام عليكم");
        Assert.Equal(_writer.Reply, Assert.Single(d.Actions).Text); // «وعليكم السلام» and the greeting: one message
        Assert.Equal([TextKeys.Greeting], Keys(d));
        Assert.Equal(WrittenReply.Passed, d.Effects!.Written!.Guard);
        Assert.Contains("السلام عليكم", _writer.Last!.History[^1].Text); // his message of this turn is the last line it sees
    }

    [Fact]
    public async Task V141_a_sentence_the_check_refuses_sends_the_approved_texts_unchanged()
    {
        await InSituation("urgent");
        ConversationalOn();
        _writer.Reply = "متقلقش، المستشار هيكلمك حالًا والنفقة عقوبتها حبس شهر.";
        var d = await Say("انت فاكرني؟");
        Assert.Equal("أكيد 👋 عاجل مدفوع، المستشار هيكلم حضرتك خلال دقايق.", d.Actions[^1].Text);
        Assert.Equal("legal_word", d.Effects!.Written!.Guard);
        Assert.Null(d.Effects.Written.Text);
    }

    [Fact]
    public async Task V141_the_writer_down_sends_the_approved_texts()
    {
        await InSituation("urgent");
        ConversationalOn();
        _writer.Reply = null;
        var d = await Say("انت فاكرني؟");
        Assert.Equal("أكيد 👋 عاجل مدفوع، المستشار هيكلم حضرتك خلال دقايق.", d.Actions[^1].Text);
        Assert.Equal(WriterOutcome.Error, d.Effects!.Written!.Guard);
    }

    [Fact]
    public async Task V141_the_greeting_in_kind_and_the_situation_become_one_written_message_with_the_facts()
    {
        await InSituation("urgent");
        ConversationalOn();
        _writer.Reply = "وعليكم السلام 👋 طلبك العاجل قدام المستشار، وهيكلم حضرتك خلال دقايق.";
        var d = await Say("السلام عليكم");
        Assert.Equal(_writer.Reply, Assert.Single(d.Actions).Text);
        Assert.Equal([TextKeys.SituationUrgent], Keys(d));
        Assert.Contains("خلال دقايق", _writer.Last!.Approved); // the facts it was given
        Assert.StartsWith("وعليكم السلام", _writer.Last.Approved);
    }

    [Fact]
    public async Task V141_buttons_stay_under_the_written_sentence()
    {
        ApproveTask16Texts();
        ConversationalOn();
        _writer.Reply = "معلش لو مش واضح، تحب تشوف الخدمات ولا تكلم حد من المكتب؟";
        await Say("خدمات");
        var d = await Say("اي خدمات المكتب"); // the same answer again: repeat_choice, its two buttons
        var a = Assert.Single(d.Actions);
        Assert.Equal(ActionType.SendInteractive, a.Type);
        Assert.Equal(_writer.Reply, a.Text);
        Assert.Equal([ServiceRow.Id(ServiceRow.Menu), ServiceRow.Id(ServiceRow.Human)], a.Interactive!.Choices.Select(c => c.Id));
    }

    [Fact]
    public async Task V141_where_to_pay_is_never_written()
    {
        await InSituation("new");
        ConversationalOn();
        _writer.Reply = "حوّل أي مبلغ وخلاص.";
        var d = await Send(Pressed(ServiceRow.Id(ServiceRow.Urgent)));
        Assert.DoesNotContain(d.Actions, a => a.Text == _writer.Reply);
    }

    /// <summary>The writer and the conversation it sees, in memory.</summary>
    private sealed class FakeWriter : IReplyWriter, IConversationHistory
    {
        public string? Reply { get; set; }

        public int Calls { get; private set; }

        public WriteRequest? Last { get; private set; }

        public Task<WriterOutcome> WriteAsync(WriteRequest request, HazemSettings settings, CancellationToken ct)
        {
            Calls++;
            Last = request;
            return Task.FromResult(new WriterOutcome(Reply, Reply is null ? WriterOutcome.Error : null, "fake", "write-v1", 5));
        }

        public Task<IReadOnlyList<ChatLine>> RecentAsync(string waId, int turns, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ChatLine>>([new ChatLine(false, "أنا حازم، المساعد الرقمي")]);
    }
}

/// <summary>TASK 17, part B (classify-v14): the message read with its context, and a service the AI understood answered as such.</summary>
public sealed class AiContextTests
{
    [Fact]
    public void The_context_goes_before_the_message_masked_and_the_message_reads_back()
    {
        var text = Office.Application.Understanding.AiContext.Build("ابو الليل مين", "أكيد يا ابو الليل 👋 حوّل على 01000000000",
            "انجز بالله", "استشارة عاجلة مدفوعة مستنية مكالمة المستشار");
        Assert.StartsWith(Office.Application.Understanding.AiContext.Head, text);
        Assert.DoesNotContain("01000000000", text); // masked as any AI input (T-09)
        Assert.Equal("ابو الليل مين", Office.Application.Understanding.AiContext.MessageOf(text));
        Assert.Equal("مرحبا", Office.Application.Understanding.AiContext.Build("مرحبا", null, null, null)); // nothing known: the message alone
    }
}

public sealed partial class BookingFlowTests
{
    [Fact]
    public async Task V141_asking_whether_the_office_does_one_of_its_services_is_answered_as_that_service()
    {
        await InSituation("new");
        _script["انتو بتعملو عقود عمل"] = new("office_info", "contract_drafting", "unknown", null, 0.95, InfoTopic: "service_check");
        var d = await Say("انتو بتعملو عقود عمل");
        Assert.DoesNotContain(TextKeys.ServiceCheck, Keys(d)); // never «هسأل المكتب» for the office's own service
        Assert.Contains(PendingTask.ServiceRequest, TaskKinds(d));

        _script["بتطلعوا رخصة محل؟"] = new("office_info", null, "unknown", null, 0.95, InfoTopic: "service_check");
        _texts[TextKeys.ServiceCheck] = new ApprovedText(TextKeys.ServiceCheck, 1, "هسأل المكتب عن ده، ويردوا على حضرتك هنا.", 300, true);
        Assert.Contains(TextKeys.ServiceCheck, Keys(await Say("بتطلعوا رخصة محل؟"))); // not on the list: the office answers
    }
}
