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
    internal static WriteRequest R(string approved) => new("purpose", approved, "", []);

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

    [Fact]
    public void A_fact_of_the_office_may_answer_his_question_and_a_number_outside_the_facts_may_not()
    {
        // v1.45 (TASK 18): «مدتها قد إيه؟» answered from «حقايق المكتب»; «1,000» and «1000» are one number
        var asked = R("المستشار يقدر يشوف الموضوع مع حضرتك في استشارة: بالتليفون بـ500 أو في المكتب بـ1,000.")
            with { Facts = "استشارة التليفون أو الفيديو ربع ساعة بـ500 جنيه.\nالمقابلة في المكتب نص ساعة بـ1,000 جنيه." };
        Assert.Null(ReplyGuard.Problem("مدتها ربع ساعة بـ500 جنيه، والمقابلة في المكتب نص ساعة بـ1000. تحب أنهي واحدة؟", asked, null));
        Assert.Equal("number_not_given", ReplyGuard.Problem("مدتها 20 دقيقة بـ500. تحب أنهي واحدة؟", asked, null));
    }

    [Theory]
    [InlineData("المستشار هو اللي هيجاوب حضرتك على ده في الاستشارة. تحب تحجز؟")] // four words of the refusal sent just before
    [InlineData("السعر اتقال فوق، تحب تحجز؟")] // a pointer to it
    [InlineData("تحب تحجز استشارة إيهؤ")] // a misspelt end
    public void What_was_sent_before_in_the_turn_is_never_said_again_and_a_typo_never_goes(string reply)
    {
        var request = R("المستشار يقدر يشوف الموضوع مع حضرتك في استشارة.") with { Before = "المستشار هو اللي هيجاوب حضرتك على ده في الاستشارة." };
        Assert.NotNull(ReplyGuard.Problem(reply, request, null));
        Assert.Null(ReplyGuard.Problem("لو تحب، المستشار يقدر يشوف الموضوع مع حضرتك في استشارة.", request, null));
    }

    [Theory]
    [InlineData(null, "عايزه اخلع جوزي", ClientGender.Woman)]
    [InlineData("male", "محتاجه استشارة", ClientGender.Woman)] // her own words win over the name's gender
    [InlineData("female", "السلام عليكم", ClientGender.Woman)]
    [InlineData("male", "عايز احجز", ClientGender.Man)]
    [InlineData(null, "عايز احجز", ClientGender.Unknown)]
    public void The_client_s_gender_comes_from_the_name_and_from_her_own_words(string? nameGender, string said, string expected) =>
        Assert.Equal(expected, ClientGender.Of(nameGender, [new ChatLine(true, said)]));

    [Theory]
    [InlineData("1️⃣ حوّل 500 جنيه على InstaPay: office@instapay.", "1️⃣ حوّلي 500 جنيه على InstaPay: office@instapay.")]
    [InlineData("اختار الميعاد اللي يناسبك من القايمة 👇", "اختاري الميعاد اللي يناسبك من القايمة 👇")]
    [InlineData("لو حوّلت دلوقتي، ابعت الصورة هنا.", "لو حوّلتي دلوقتي، ابعتي الصورة هنا.")]
    [InlineData("تحبي تكوني أنهي واحدة؟", "تحبي تكوني أنهي واحدة؟")] // already hers: unchanged
    [InlineData("صورة التحويل وصلت ✅", "صورة التحويل وصلت ✅")] // «التحويل» is no verb to him
    public void To_a_woman_the_approved_words_to_him_are_said_to_her_and_nothing_else_changes(string text, string expected) =>
        Assert.Equal(expected, ClientGender.Feminine(text));

    [Theory]
    [InlineData(TextKeys.UrgentPay)]
    [InlineData(TextKeys.BookingHold)]
    [InlineData(TextKeys.BookingPaid)]
    [InlineData(TextKeys.ServicesMenu)]
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

    [Fact]
    public async Task V145_a_legal_refusal_written_without_the_consultation_sends_the_approved_texts()
    {
        await InSituation("new");
        ConversationalOn();
        _script["الحضانة بتبقى لمين"] = new("legal_question", null, "unknown", null, 0.9);
        _writer.Reply = "الحضانة بتفرق من حالة لحالة، تحب تحجز؟"; // no lawyer, no consultation: the refusal dropped
        var d = await Say("الحضانة بتبقى لمين");
        Assert.Contains(TextKeys.LegalQuestion, Keys(d));
        Assert.DoesNotContain(d.Actions, a => a.Text == _writer.Reply);
        Assert.Equal("refusal_dropped", d.Effects!.Written!.Guard);
    }

    [Fact]
    public async Task V145_a_legal_refusal_and_the_offer_become_one_written_message()
    {
        await InSituation("new");
        ConversationalOn();
        _script["الحضانة بتبقى لمين"] = new("legal_question", null, "unknown", null, 0.9);
        _writer.Reply = "سؤال الحضانة ده المستشار سعد بنفسه هو اللي هيجاوب حضرتك عليه في الاستشارة. تحب تحجز؟";
        var d = await Say("الحضانة بتبقى لمين");
        Assert.Equal(WrittenReply.Passed, d.Effects!.Written!.Guard);
        Assert.Single(d.Actions, a => a.Text == _writer.Reply);
        Assert.DoesNotContain(d.Actions, a => a.Text == _texts[TextKeys.LegalQuestion].Text); // never sent again beside it
    }

    private void HandoffOn()
    {
        _settings[HazemResponder.HandoffEnabledKey] = "true";
        _texts[TextKeys.HumanRequest] = new ApprovedText(TextKeys.HumanRequest, 2, "تمام، بلّغت المكتب إن حضرتك عايز تكلم حد، وحد من المكتب هيرد على حضرتك هنا.", 300, true);
        _texts[TextKeys.HandoffWaiting] = new ApprovedText(TextKeys.HandoffWaiting, 1, "رسالة حضرتك وصلت ✅ وحد من المكتب هيرد على حضرتك هنا في أقرب وقت.", 300, true);
        _texts[TextKeys.NoPersonalNumber] = new ApprovedText(TextKeys.NoPersonalNumber, 1, "معلش، أرقام المستشار الشخصية مش بنديها.", 300, true);
        _script["عايز اكلم حد من المكتب"] = new("human_request", null, "unknown", null, 0.9);
        _script["ممكن رقم الاستاذ الشخصي"] = new("human_request", null, "unknown", null, 0.9);
        _script["هل مبلغ الاستشاره بيتخصم من الاتعاب"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "fee_policy");
    }

    [Fact]
    public async Task V148_after_the_handoff_his_first_message_hears_it_arrived_once_then_hazem_is_silent()
    {
        await InSituation("new");
        HandoffOn();
        Assert.Equal([TextKeys.HumanRequest], Keys(await Say("عايز اكلم حد من المكتب")));
        Assert.Equal([TextKeys.HandoffWaiting], Keys(await Say("هل مبلغ الاستشاره بيتخصم من الاتعاب")));
        Assert.Empty((await Say("هل مبلغ الاستشاره بيتخصم من الاتعاب")).Actions); // once: silent again until the office answers
    }

    [Fact]
    public async Task V148_the_lawyer_s_personal_number_is_not_given_and_is_no_handoff()
    {
        await InSituation("new");
        HandoffOn();
        var d = await Say("ممكن رقم الاستاذ الشخصي");
        Assert.Equal([TextKeys.NoPersonalNumber], Keys(d));
        Assert.Empty(d.Effects!.Tasks); // nobody was told: no task, no handoff
        Assert.Equal([ServiceRow.Id(ServiceRow.Menu), ServiceRow.Id(ServiceRow.Human)], d.Actions[^1].Interactive!.Choices.Select(c => c.Id));
        Assert.NotEmpty((await Say("هل مبلغ الاستشاره بيتخصم من الاتعاب")).Actions); // Hazem still answers
    }

    [Theory]
    [InlineData(true, "أيوه، الاستشارة دايمًا مع المستشار سعد فتحي سعد نفسه.", false)] // answered from the facts: no task
    [InlineData(false, "سؤال مهم، هوصّله للمكتب، ويردوا على حضرتك هنا.", true)]       // not in them: the office answers it
    [InlineData(true, "أيوه، مع المستشار نفسه، وهوصّله للمكتب كمان.", true)]          // both at once: refused, as v1.46
    public async Task V148_a_service_question_the_office_facts_answer_needs_no_task(bool fromFacts, string reply, bool task)
    {
        await InSituation("new");
        ConversationalOn();
        _texts[TextKeys.ServiceDetail] = new ApprovedText(TextKeys.ServiceDetail, 1, "سؤال مهم 👌 هوصّله للمكتب، ويردوا على حضرتك هنا.", 300, true);
        _texts[TextKeys.OfficeFacts] = new ApprovedText(TextKeys.OfficeFacts, 1, "الاستشارة دايمًا مع المستشار سعد فتحي سعد نفسه.", 600, true);
        _script["هقابل المستشار سعد نفسه؟"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "service_detail");
        _writer.Reply = reply;
        _writer.FromFacts = fromFacts;
        var d = await Say("هقابل المستشار سعد نفسه؟");
        Assert.Equal(task, d.Effects!.Tasks.Any(t => t.Kind == PendingTask.ServiceQuestion));
    }

    [Fact]
    public async Task V148_the_office_s_number_is_a_request_for_the_office_as_before()
    {
        await InSituation("new");
        HandoffOn();
        _script["ممكن رقم المكتب اتصل بيكم"] = new("human_request", null, "unknown", null, 0.9);
        var d = await Say("ممكن رقم المكتب اتصل بيكم");
        Assert.Equal([TextKeys.HumanRequest], Keys(d));
        Assert.Equal(PendingTask.HumanRequested, Assert.Single(d.Effects!.Tasks).Kind);
    }

    [Fact]
    public void V148_the_handoff_reply_never_asks_back_and_the_writer_never_promises_to_tell_the_office()
    {
        var offer = ReplyGuardTests.R("المستشار يقدر يشوف الموضوع مع حضرتك في استشارة.");
        Assert.Equal("promise", ReplyGuard.Problem("سؤالك مهم، هوصّله للمكتب. تحب تحجز؟", offer, null));
        Assert.Null(ReplyGuard.Problem("تمام، بلّغت المكتب، وحد من المكتب هيرد على حضرتك هنا.",
            ReplyGuardTests.R("تمام، بلّغت المكتب إن حضرتك عايز تكلم حد، وحد من المكتب هيرد على حضرتك هنا."), null));
    }

    /// <summary>The writer and the conversation it sees, in memory.</summary>
    private sealed class FakeWriter : IReplyWriter, IConversationHistory
    {
        public string? Reply { get; set; }

        public int Calls { get; private set; }

        public WriteRequest? Last { get; private set; }

        public bool FromFacts { get; set; }

        public Task<WriterOutcome> WriteAsync(WriteRequest request, HazemSettings settings, CancellationToken ct)
        {
            Calls++;
            Last = request;
            return Task.FromResult(new WriterOutcome(Reply, Reply is null ? WriterOutcome.Error : null, "fake", "write-v1", 5) { FromFacts = FromFacts });
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

public sealed partial class BookingFlowTests
{
    [Fact]
    public async Task V143_asking_about_a_contract_opens_no_order_and_the_start_button_does()
    {
        // 2026-11-01 03:39: «لو عاوز اعمل عقد عمل بتعلوه» opened an order, then «انت فاهمني طيب؟» got «طلبك مستني الورق».
        await InSituation("new");
        ServicesOn();
        _texts[TextKeys.BtnStartOrder] = new ApprovedText(TextKeys.BtnStartOrder, 1, "✅ ابدأ الطلب", 20, true);
        _texts[TextKeys.ContractDraftingRequest] = new ApprovedText(TextKeys.ContractDraftingRequest, 1, "صياغة العقد بتبدأ من 3,000 جنيه.", 300, true);
        _script["لو عاوز اعمل عقد عمل بتعلوه"] = new("service_by_name", "contract_drafting", "unknown", null, 0.95);
        _script["انت فاهمني طيب ؟"] = new("other", "contract_drafting", "unknown", null, 0.95);
        var asked = await Say("لو عاوز اعمل عقد عمل بتعلوه");
        Assert.Contains(TextKeys.ContractDraftingRequest, Keys(asked));
        Assert.Empty(_booking.Orders);
        Assert.DoesNotContain(TextKeys.SituationOrder, Keys(await Say("انت فاهمني طيب ؟")));
        Assert.Empty(_booking.Orders);

        await Press(ServiceRow.Id(ServiceRow.Drafting)); // «✅ ابدأ الطلب»
        Assert.Equal(("contract_drafting", "collecting"), (Assert.Single(_booking.Orders).Kind, _booking.Orders[0].State));
    }
}
