using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Domain.Clients;
using Office.Domain.Common;

namespace Office.UnitTests;

/// <summary>TASK 04 section 5: the conversation flow with a fake classifier, store and texts.</summary>
public sealed class HazemFlowTests
{
    private const string Number = "201000000041";

    /// <summary>The active texts of migration 0005 (DECISIONS word for word).</summary>
    public static readonly IReadOnlyDictionary<string, string> Approved = new Dictionary<string, string>
    {
        [TextKeys.WelcomeAskName] = "أهلًا بحضرتك في مكتب الأستاذ سعد فتحي سعد للمحاماة، معاك حازم. ممكن اسم حضرتك رباعي؟",
        [TextKeys.ConsultationOffer] = "الأستاذ يقدر يشوف الموضوع مع حضرتك في استشارة: بالتليفون بـ500 أو في المكتب بـ1,000.",
        [TextKeys.LegalQuestion] = "الأستاذ هو اللي هيجاوب حضرتك على ده في الاستشارة.",
        [TextKeys.UrgentPrice] = "الاستشارة العاجلة بالتليفون ربع ساعة بـ1,000 جنيه.",
        [TextKeys.DocumentReviewRequest] = "تحت أمرك. مراجعة العقد بتبدأ من 1,500 جنيه. ابعتلي صورة العقد هنا.",
        [TextKeys.WrittenOpinion] = "الرأي المكتوب بيكون بعد الاستشارة، وسعره سعر الاستشارة + 2,000 جنيه.",
        [TextKeys.UrfUnclear] = "تحت أمرك، قولّي المطلوب",
        [TextKeys.UrfProof] = "إثبات الجواز بيكون بدعوى قدام محكمة الأسرة، والأستاذ هيحدد المطلوب حسب الورق اللي معاك.",
    };

    private readonly FakeStore _store = new();
    private readonly FakeClassifier _ai = new();
    private readonly Dictionary<string, ApprovedText> _texts = Approved.ToDictionary(kv => kv.Key, kv => new ApprovedText(kv.Key, 1, kv.Value, 300, true));
    private HazemSettings _settings = new(new Dictionary<string, string> { [HazemResponder.RecipientsKey] = Number });
    private int _turn;

    private HazemResponder Hazem => new(_store, _ai, new FakeTexts(_texts), new NoLists(), TimeProvider.System);

    /// <summary>Sends one text turn, saves its effects like the real store, returns the text keys sent.</summary>
    private async Task<(List<string> Keys, TurnDecision Decision)> Say(string text, string waId = Number) =>
        await Send(new IncomingMessage($"wamid.{++_turn}", MessageType.Text, text, null, null), waId);

    private async Task<(List<string> Keys, TurnDecision Decision)> Send(IncomingMessage message, string waId = Number)
    {
        var d = await Hazem.DecideAsync(new Turn($"wt:{++_turn}", waId, null, [message]), _settings, CancellationToken.None);
        if (d.Effects is { } e)
        {
            _store.Rows[waId] = e.State;
        }

        return (d.Effects?.Replies.Select(r => r.Key).ToList() ?? [], d);
    }

    private void Activate(params string[] keys)
    {
        foreach (var k in keys)
        {
            _texts[k] = new ApprovedText(k, 1, "PROPOSED " + k, 300, true);
        }
    }

    private async Task Known()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
    }

    // ---------- B-01 / B-02 ----------

    [Fact]
    public async Task First_message_gets_the_welcome_and_after_the_name_hazem_answers_the_saved_request()
    {
        var (first, d1) = await Say("السلام عليكم عايز أحجز استشارة");
        Assert.Equal([TextKeys.WelcomeAskName], first);
        Assert.Equal(Approved[TextKeys.WelcomeAskName], Assert.Single(d1.Actions).Text);
        Assert.Equal("consultation_request", _store.Rows[Number].Intake.Pending?.Intent);

        var (second, _) = await Say("محمد أحمد علي حسن");
        Assert.Equal([TextKeys.ConsultationOffer], second);
        var s = _store.Rows[Number];
        Assert.True(s.Person.HasFullName);
        Assert.Equal("محمد أحمد علي حسن", s.Person.Name?.Normalized);
        Assert.Equal((IntakeState.OfferSent, ClientType.NewClient), (s.Intake.State, s.Intake.ClientType));
        Assert.Null(s.Intake.Pending);
    }

    [Fact]
    public async Task Legal_question_first_then_name_gets_b20_and_the_offer()
    {
        await Say("ينفع أطلق مراتي غيابي؟");
        Assert.Equal([TextKeys.LegalQuestion, TextKeys.ConsultationOffer], (await Say("منى سعيد عبد الله حسن")).Keys);
    }

    [Fact]
    public async Task Name_without_a_request_gets_the_consultation_offer()
    {
        await Say("السلام عليكم");
        Assert.Equal([TextKeys.ConsultationOffer], (await Say("محمد أحمد علي حسن")).Keys);
    }

    [Fact]
    public async Task Short_name_is_asked_once_more_then_hazem_carries_on()
    {
        await Say("السلام عليكم");
        var (again, d) = await Say("محمد أحمد");
        Assert.Equal([TextKeys.WelcomeAskName], again); // ask_name_again is not approved yet → B-01 text
        Assert.Equal(TextKeys.AskNameAgain, Assert.Single(d.Effects!.Replies).InsteadOf);

        Assert.Equal([TextKeys.ConsultationOffer], (await Say("محمد أحمد")).Keys);
        var s = _store.Rows[Number];
        Assert.Equal(false, s.Person.FullNameProvided);
        Assert.Equal("محمد أحمد", s.Person.Name?.Raw);
    }

    [Fact]
    public async Task Approved_ask_name_again_is_used_once_approved()
    {
        Activate(TextKeys.AskNameAgain);
        await Say("السلام عليكم");
        Assert.Equal([TextKeys.AskNameAgain], (await Say("مش هقول")).Keys);
    }

    [Fact]
    public async Task Known_number_with_a_full_name_is_never_asked_again()
    {
        await Known();
        var (keys, _) = await Say("عايز أحجز استشارة");
        Assert.Equal([TextKeys.ConsultationOffer], keys);
        Assert.DoesNotContain(TextKeys.WelcomeAskName, keys);
    }

    // ---------- section 5.2: every intent ----------

    [Theory]
    [InlineData("عايز أحجز", new[] { TextKeys.ConsultationOffer }, null)]
    [InlineData("ينفع أطلق؟", new[] { TextKeys.LegalQuestion, TextKeys.ConsultationOffer }, null)]
    [InlineData("مستعجل جدا", new[] { TextKeys.UrgentPrice, TextKeys.ConsultationOffer }, PendingTask.UrgentRequest)]
    [InlineData("عايز مراجعة عقد", new[] { TextKeys.DocumentReviewRequest }, PendingTask.ServiceRequest)]
    [InlineData("عايز رأي مكتوب", new[] { TextKeys.WrittenOpinion, TextKeys.ConsultationOffer }, null)]
    [InlineData("أثبت جوازي العرفي", new[] { TextKeys.UrfProof, TextKeys.ConsultationOffer }, null)]
    [InlineData("عرفي", new[] { TextKeys.UrfUnclear }, null)]
    [InlineData("عايز صياغة عقد", new[] { TextKeys.ConsultationOffer }, PendingTask.ServiceRequest)]
    [InlineData("مراجعة العقد بكام", new[] { TextKeys.ConsultationOffer }, null)]
    [InlineData("الاستشارة بكام", new[] { TextKeys.ConsultationOffer }, null)]
    [InlineData("القضية بكام", new[] { TextKeys.ConsultationOffer }, null)]
    [InlineData("بخصوص القضية بتاعتي", new string[0], PendingTask.ExistingClientMessage)]
    [InlineData("عايز أكلم موظف", new string[0], PendingTask.HumanRequested)]
    [InlineData("عايز ألغي الميعاد", new string[0], PendingTask.RescheduleOrCancel)]
    [InlineData("صباح الخير", new[] { TextKeys.ConsultationOffer }, null)]
    [InlineData("شكرا", new string[0], null)]
    [InlineData("ههههه", new[] { TextKeys.ConsultationOffer }, null)]
    public async Task Each_intent_gets_its_approved_text_or_its_fallback(string text, string[] keys, string? task)
    {
        await Known();
        var (sent, d) = await Say(text);
        Assert.Equal(keys, sent);
        Assert.Equal(task is null ? [] : [task], d.Effects!.Tasks.Select(t => t.Kind));
        Assert.All(d.Actions, a => Assert.Contains(a.Text, _texts.Values.Select(t => t.Text))); // nothing but approved texts
    }

    [Theory]
    [InlineData("ههههه", TextKeys.Unclear)]
    [InlineData("صباح الخير", TextKeys.Greeting)]
    [InlineData("شكرا", TextKeys.Thanks)]
    [InlineData("عايز أكلم موظف", TextKeys.HumanRequest)]
    [InlineData("بخصوص القضية بتاعتي", TextKeys.ExistingMatter)]
    [InlineData("عايز صياغة عقد", TextKeys.ContractDraftingRequest)]
    [InlineData("مراجعة العقد بكام", TextKeys.PriceDocumentReview)]
    [InlineData("القضية بكام", TextKeys.PriceCaseRepresentation)]
    public async Task Proposed_texts_go_out_with_their_key_once_approved(string text, string key)
    {
        Activate(typeof(TextKeys).GetFields().Select(f => (string)f.GetValue(null)!).Where(k => !Approved.ContainsKey(k)).ToArray());
        await Known();
        var (_, d) = await Say(text);
        // v1.21: a greeting's answer in kind (greet_*) may open the reply; the intent's text follows it.
        var first = d.Effects!.Replies.First(r => !r.Key.StartsWith("greet_", StringComparison.Ordinal));
        Assert.Equal((key, 1, (string?)null), (first.Key, first.Version, first.InsteadOf));
    }

    // ---------- v1.21 (B-01.6): greetings answered in kind ----------

    private static readonly string[] GreetKeys =
    [
        TextKeys.GreetSalam, TextKeys.GreetRamadan, TextKeys.GreetJumua, TextKeys.GreetOccasion, TextKeys.GreetMorning,
        TextKeys.GreetEvening, TextKeys.GreetHowAreYou, TextKeys.GreetGoodNight, TextKeys.GreetGoodbye,
    ];

    [Fact]
    public async Task The_first_salam_is_returned_before_the_welcome()
    {
        Activate(GreetKeys);
        var (keys, d) = await Say("السلام عليكم");
        Assert.Equal([TextKeys.GreetSalam, TextKeys.WelcomeAskName], keys);
        Assert.Equal("PROPOSED " + TextKeys.GreetSalam, d.Actions[0].Text);
    }

    [Fact]
    public async Task A_known_client_s_good_morning_gets_good_morning_then_the_answer()
    {
        Activate(GreetKeys);
        await Known();
        var (keys, _) = await Say("صباح الخير");
        Assert.Equal(TextKeys.GreetMorning, keys[0]);
        Assert.True(keys.Count > 1); // Hazem's answer follows
    }

    [Fact]
    public async Task A_goodbye_alone_is_answered_alone_without_the_ai()
    {
        Activate(GreetKeys);
        await Known();
        var asked = _ai.Texts.Count;
        var (keys, d) = await Say("مع السلامة");
        Assert.Equal([TextKeys.GreetGoodbye], keys);
        Assert.Equal(asked, _ai.Texts.Count);
        Assert.Null(d.Effects!.Classification);
    }

    [Fact]
    public async Task A_goodbye_as_the_first_message_still_gets_the_welcome()
    {
        Activate(GreetKeys);
        var (keys, _) = await Say("مع السلامة");
        Assert.Equal([TextKeys.GreetGoodbye, TextKeys.WelcomeAskName], keys);
    }

    [Fact]
    public async Task No_greeting_is_answered_while_its_text_is_not_approved()
    {
        var (keys, _) = await Say("السلام عليكم");
        Assert.Equal([TextKeys.WelcomeAskName], keys);
    }

    [Fact]
    public async Task Second_unclear_message_in_a_row_becomes_a_task_and_a_clear_one_resets_the_count()
    {
        await Known();
        Assert.Equal([TextKeys.ConsultationOffer], (await Say("ههههه")).Keys);
        var (second, d) = await Say("ههههه");
        Assert.Empty(second);
        Assert.Equal(PendingTask.UnclearMessage, Assert.Single(d.Effects!.Tasks).Kind);
        await Say("عايز أحجز");
        Assert.Equal([TextKeys.ConsultationOffer], (await Say("ههههه")).Keys);
    }

    // ---------- fallbacks: the AI and the texts ----------

    [Fact]
    public async Task Ai_down_the_client_still_gets_an_answer()
    {
        await Known();
        _ai.Down = true;
        var (keys, d) = await Say("عايز أحجز استشارة");
        Assert.Equal([TextKeys.ConsultationOffer], keys);
        Assert.Equal(ClassifierOutcome.Error, d.Effects!.Classification!.FallbackReason);
        Assert.Contains(d.Notes!, n => n.Contains("fell back", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refusal_is_never_saved_as_the_name()
    {
        await Say("السلام عليكم");
        await Say("مش هقول اسمي");
        Assert.Equal([TextKeys.ConsultationOffer], (await Say("مش هقول اسمي")).Keys);
        var p = _store.Rows[Number].Person;
        Assert.Null(p.Name);
        Assert.Equal(false, p.FullNameProvided);
    }

    [Theory]
    [InlineData("عايز احجز استشارة")]
    [InlineData("مش هقول اسمي")]
    [InlineData("السلام عليكم يا استاذ")]
    public async Task Ai_down_a_request_or_refusal_is_not_taken_as_a_name(string text)
    {
        await Say("السلام عليكم");
        _ai.Down = true;
        await Say(text);
        Assert.Null(_store.Rows[Number].Person.Name);
    }

    [Fact]
    public async Task Ai_down_a_name_after_اسمي_is_taken()
    {
        await Say("السلام عليكم");
        _ai.Down = true;
        await Say("اسمي منى سعيد عبد الله حسن");
        Assert.Equal("منى سعيد عبد الله حسن", _store.Rows[Number].Person.Name?.Normalized);
        Assert.True(_store.Rows[Number].Person.HasFullName);
    }

    [Fact]
    public async Task A_photo_instead_of_the_name_does_not_use_up_the_extra_ask()
    {
        await Say("السلام عليكم");
        Assert.Equal([TextKeys.WelcomeAskName], (await Send(Image())).Keys); // asked again, not counted
        Assert.Equal(1, _store.Rows[Number].Intake.NameAsks);
        Assert.Equal([TextKeys.WelcomeAskName], (await Say("محمد أحمد")).Keys); // still the one extra ask
        Assert.Null(_store.Rows[Number].Person.FullNameProvided);
    }

    [Fact]
    public async Task Ai_down_a_typed_name_is_still_taken()
    {
        await Say("السلام عليكم");
        _ai.Down = true;
        await Say("محمد أحمد علي حسن");
        Assert.True(_store.Rows[Number].Person.HasFullName);
    }

    [Fact]
    public async Task A_text_with_a_missing_office_value_is_not_sent_and_the_reason_is_kept()
    {
        _texts[TextKeys.LegalQuestion] = new ApprovedText(TextKeys.LegalQuestion, 2, "تعالى على {{OFFICE_ADDRESS}}", 300, true);
        await Known();
        var (keys, d) = await Say("ينفع أطلق؟");
        Assert.Equal([TextKeys.ConsultationOffer], keys);
        Assert.Contains(d.Notes!, n => n == $"text {TextKeys.LegalQuestion} not sent: {TextRenderer.MissingValue}");
        Assert.DoesNotContain(d.Actions, a => a.Text!.Contains("OFFICE_ADDRESS", StringComparison.Ordinal));
    }

    // ---------- media, recipients, handoff ----------

    private static IncomingMessage Image(MediaStatus status = MediaStatus.Stored) =>
        new("wamid.img", MessageType.Image, null, null, null,
            Media: new MediaRef(status, status == MediaStatus.Stored ? "office-sensitive" : null, status == MediaStatus.Stored ? "wa/2026/10/a.jpg" : null));

    [Fact]
    public async Task A_photo_without_words_never_reaches_the_ai()
    {
        await Known();
        var calls = _ai.Texts.Count;
        var (keys, d) = await Send(Image());
        Assert.Empty(keys); // media_received is proposed only
        Assert.Equal(calls, _ai.Texts.Count);
        Assert.Null(d.Effects!.Classification);
    }

    [Theory]
    [InlineData(MediaStatus.Stored, TextKeys.MediaReceived)]
    [InlineData(MediaStatus.TooLarge, TextKeys.MediaTooLarge)]
    [InlineData(MediaStatus.Failed, TextKeys.MediaFailed)]
    [InlineData(MediaStatus.Rejected, TextKeys.MediaRejected)]
    public async Task Media_gets_its_proposed_text_once_approved(MediaStatus status, string key)
    {
        Activate(key);
        await Known();
        Assert.Equal([key], (await Send(Image(status))).Keys);
    }

    [Fact]
    public async Task Reactions_and_stickers_get_no_answer()
    {
        Activate(TextKeys.MediaReceived);
        await Known();
        Assert.Empty((await Send(new IncomingMessage("wamid.r", MessageType.Reaction, null, null, null, Reaction: new Reaction("👍", "x")))).Keys);
        Assert.Empty((await Send(new IncomingMessage("wamid.s", MessageType.Sticker, null, null, null, Media: new MediaRef(MediaStatus.Stored)))).Keys);
    }

    [Fact]
    public async Task A_number_outside_hazem_recipients_gets_nothing_and_reaches_no_ai()
    {
        var (keys, d) = await Say("عايز أحجز", waId: "201000000099");
        Assert.Empty(keys);
        Assert.Null(d.Effects);
        Assert.Empty(_ai.Texts);
    }

    [Fact]
    public async Task Open_to_all_answers_any_number()
    {
        _settings = new(new Dictionary<string, string> { [HazemResponder.OpenToAllKey] = "true" });
        Assert.Equal([TextKeys.WelcomeAskName], (await Say("السلام عليكم", waId: "201000000099")).Keys);
    }

    [Fact]
    public async Task While_staff_hold_the_conversation_hazem_is_silent()
    {
        await Known();
        _store.Rows[Number].Conversation.StaffReplied(DateTimeOffset.UtcNow);
        var (keys, d) = await Say("عايز أحجز");
        Assert.Empty(keys);
        Assert.Null(d.Effects!.Classification);
    }

    [Fact]
    public async Task Hazem_comes_back_12_hours_after_the_staff()
    {
        await Known();
        _store.Rows[Number].Conversation.StaffReplied(DateTimeOffset.UtcNow.AddHours(-13));
        Assert.Equal([TextKeys.ConsultationOffer], (await Say("عايز أحجز")).Keys);
    }

    [Fact]
    public async Task Every_reply_records_its_text_key_and_version_under_a_stable_key()
    {
        await Known();
        var (_, d) = await Say("ينفع أطلق؟");
        Assert.Equal(d.Actions.Select(a => a.IdempotencyKey), d.Effects!.Replies.Select(r => r.IdempotencyKey));
        Assert.Equal(["hazem:wt:6:0", "hazem:wt:6:1"], d.Actions.Select(a => a.IdempotencyKey));
        Assert.All(d.Effects.Replies, r => Assert.Equal(1, r.Version));
        Assert.Equal("wamid.5", d.Actions[0].ContextWamid);
        Assert.Null(d.Actions[1].ContextWamid);
    }

    [Fact]
    public async Task The_classification_record_holds_no_client_text_and_no_name()
    {
        await Say("السلام عليكم");
        var (_, d) = await Say("اسمي محمد أحمد علي حسن ورقمي 01012345678");
        var record = d.Effects!.Classification!;
        Assert.True(record.FullNamePresent);
        Assert.DoesNotContain("محمد", record.ToString());
        Assert.DoesNotContain("0101", record.ToString());
        Assert.DoesNotContain("0101", string.Join("|", _ai.Texts)); // redacted before the AI
    }

    // ---------- fakes ----------

    private sealed class FakeStore : IConversationStore
    {
        public Dictionary<string, ClientState> Rows { get; } = [];

        public Task<ClientState?> LoadAsync(string waId, CancellationToken ct) =>
            Task.FromResult(Rows.TryGetValue(waId, out var s) ? s : null);

        public Task SaveConversationAsync(Conversation conversation, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoLists : IClientLists
    {
        public Task<ListMatch> FindAsync(string waId, CancellationToken ct) => Task.FromResult(ListMatch.None);
    }

    private sealed class FakeTexts(Dictionary<string, ApprovedText> texts) : IApprovedTexts
    {
        public Task<IReadOnlyDictionary<string, ApprovedText>> GetActiveAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, ApprovedText>>(texts.Where(kv => kv.Value.Active).ToDictionary());
    }

    /// <summary>Classifies by keywords, like the e2e stub. Records what it was given.</summary>
    public sealed class FakeClassifier : IIntentClassifier
    {
        public List<string> Texts { get; } = [];

        public bool Down { get; set; }

        public Task<ClassifierOutcome> ClassifyAsync(string text, HazemSettings settings, CancellationToken ct)
        {
            Texts.Add(text);
            return Task.FromResult(Down
                ? new ClassifierOutcome(null, ClassifierOutcome.Error, "fake", "classify-v1", 1)
                : new ClassifierOutcome(Classify(text), null, "fake", "classify-v1", 1, 10, 2));
        }

        public static RawClassification Classify(string t)
        {
            RawClassification R(string intent, string? service = null) => new(intent, service, "unknown", null, 0.9);
            if (t.StartsWith("اسمي ", StringComparison.Ordinal))
            {
                return new("provide_name", null, "unknown", t[5..t.IndexOf(" ورقمي", StringComparison.Ordinal)], 0.95);
            }

            return t switch
            {
                _ when t.Contains("مستعجل", StringComparison.Ordinal) => R("urgent"),
                _ when t.Contains("بكام", StringComparison.Ordinal) => R("price_question",
                    t.Contains("مراجعة", StringComparison.Ordinal) ? "document_review" : t.Contains("القضية", StringComparison.Ordinal) ? "case_representation" : "consultation_phone_video"),
                _ when t.Contains("القضية", StringComparison.Ordinal) => R("existing_matter"),
                _ when t.Contains("موظف", StringComparison.Ordinal) => R("human_request"),
                _ when t.Contains("ألغي", StringComparison.Ordinal) => R("reschedule_or_cancel"),
                _ when t.Contains("مراجعة عقد", StringComparison.Ordinal) => R("service_by_name", "document_review"),
                _ when t.Contains("صياغة", StringComparison.Ordinal) => R("service_by_name", "contract_drafting"),
                _ when t.Contains("رأي مكتوب", StringComparison.Ordinal) => R("service_by_name", "written_opinion"),
                _ when t.Contains("أثبت", StringComparison.Ordinal) => R("service_by_name", "urf_proof"),
                _ when t == "عرفي" => R("service_by_name", "urf_unclear"),
                _ when t.Contains("أطلق", StringComparison.Ordinal) => R("legal_question"),
                _ when t.Contains("أحجز", StringComparison.Ordinal) => R("consultation_request"),
                _ when t.Contains("شكرا", StringComparison.Ordinal) => R("thanks_or_closing"),
                _ when t.Contains("السلام", StringComparison.Ordinal) || t.Contains("صباح", StringComparison.Ordinal) => R("greeting"),
                _ when t.StartsWith("مش ", StringComparison.Ordinal) => R("other"), // a refusal: the AI does not call it a name
                _ when PersonName.TryParse(t, out _) => new("provide_name", null, "unknown", t, 0.9),
                _ => R("other"),
            };
        }
    }
}
