using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Domain.Clients;
using Office.Domain.Common;
using Office.Domain.Tasks;

namespace Office.UnitTests;

/// <summary>TASK 05a: lists (B-14, B-04, B-01.5), source (B-13), topic (B-25), abuse (S-01), tasks (S-05), handoff (B-12).</summary>
public sealed class Task05aFlowTests
{
    private const string Number = "201000000041";
    private static readonly Guid Staff = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private readonly Dictionary<string, ClientState> _rows = [];
    private readonly Dictionary<string, RawClassification> _script = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApprovedText> _texts = [];
    private ListMatch _list = ListMatch.None;
    private Dictionary<string, string> _settings = new() { [HazemResponder.RecipientsKey] = Number };
    private int _turn;

    public Task05aFlowTests()
    {
        // As in the database after 0007/0008: DECISIONS texts and the TASK 04 proposals active, 05a proposals not.
        foreach (var (k, v) in HazemFlowTests.Approved)
        {
            Add(k, v);
        }

        foreach (var k in new[]
                 {
                     TextKeys.AskNameAgain, TextKeys.Unclear, TextKeys.UnclearAgain, TextKeys.Greeting, TextKeys.Thanks,
                     TextKeys.HumanRequest, TextKeys.ExistingMatter, TextKeys.RescheduleOrCancel,
                 })
        {
            Add(k, "T:" + k);
        }

        Add(TextKeys.ExistingOrNew, "حضرتك بتتابع موضوع قائم مع المكتب، ولا عايز استشارة في موضوع جديد؟");
        Add(TextKeys.AskTopic, "ولو تحب، قولّي باختصار الموضوع عن إيه عشان المستشار يبقى عارفه.");
        Add(TextKeys.TopicReceived, "تمام، وصلت ✅");
        Add(TextKeys.AbuseWarning, "T:abuse", active: false);
        Add(TextKeys.LawyerDirectReceived, "T:lawyer", active: false);
        Add(TextKeys.ColleagueReceived, "T:colleague", active: false);
    }

    private void Add(string key, string text, bool active = true) => _texts[key] = new ApprovedText(key, 1, text, 300, active);

    private void Activate(string key) => _texts[key] = _texts[key] with { Active = true };

    private HazemResponder Hazem => new(new Store(_rows), new Scripted(_script), new Texts(_texts), new Lists(() => _list), TimeProvider.System);

    private ClientState S => _rows[Number];

    private async Task<(List<string> Keys, TurnDecision D)> Say(string text, RawClassification? ai = null, Referral? referral = null)
    {
        if (ai is not null)
        {
            _script[text] = ai;
        }

        var msg = new IncomingMessage($"wamid.{++_turn}", MessageType.Text, text, null, null, Referral: referral);
        var d = await Hazem.DecideAsync(new Turn($"wt:{++_turn}", Number, null, [msg]), new HazemSettings(_settings), CancellationToken.None);
        if (d.Effects is { } e)
        {
            _rows[Number] = e.State;
        }

        return (d.Effects?.Replies.Select(r => r.Key).ToList() ?? [], d);
    }

    private static RawClassification R(string intent, string? matter = null, string? subtype = null, string role = "client", string? service = null) =>
        new(intent, service, "unknown", null, 0.9, matter, subtype, role);

    private async Task NewClientWithName()
    {
        await Say("السلام عليكم", R("greeting"));
        await Say("محمد أحمد علي حسن", new("provide_name", null, "unknown", "محمد أحمد علي حسن", 0.95));
    }

    // ---------- B-14 lists ----------

    [Fact]
    public async Task A_blocked_number_gets_nothing_is_muted_and_reaches_no_ai()
    {
        _list = new ListMatch(Blocked: true, LawyerDirect: true, ExistingClient: new ExistingClientEntry("أحمد", Staff));
        var (keys, d) = await Say("السلام عليكم", R("greeting"));
        Assert.Empty(keys);
        Assert.Null(d.Effects!.Classification);
        Assert.Equal(ConversationState.Muted, S.Conversation.State);
        Assert.Empty(d.Effects.Tasks);
    }

    [Fact]
    public async Task The_lawyer_direct_list_skips_the_name_and_makes_a_task_for_the_lawyer()
    {
        _list = new ListMatch(false, LawyerDirect: true, ExistingClient: new ExistingClientEntry("x", Staff));
        Activate(TextKeys.LawyerDirectReceived);
        var (keys, d) = await Say("أستاذ سعد عايزك ضروري", R("urgent"));
        Assert.Equal([TextKeys.LawyerDirectReceived], keys);
        Assert.Equal(PendingTask.LawyerDirectMessage, Assert.Single(d.Effects!.Tasks).Kind);
        Assert.Equal(ClientType.LawyerDirect, S.Intake.ClientType);
    }

    [Fact]
    public async Task A_listed_client_with_a_full_name_is_never_asked_for_the_name()
    {
        _list = new ListMatch(false, false, new ExistingClientEntry("محمد أحمد علي حسن", Staff));
        var (keys, d) = await Say("الجلسة الجاية امتى؟", R("existing_matter"));
        Assert.Equal([TextKeys.ExistingMatter], keys); // the neutral text: no «[المسؤول]», no confirmation of being a client
        var task = Assert.Single(d.Effects!.Tasks);
        Assert.Equal((PendingTask.ExistingClientMessage, (Guid?)Staff), (task.Kind, task.StaffId));
        Assert.True(S.Person.HasFullName);
        Assert.Equal(ClientType.ExistingClient, S.Intake.ClientType);
    }

    [Fact]
    public async Task An_existing_client_with_a_new_matter_gets_a_new_request_on_the_new_client_path()
    {
        _list = new ListMatch(false, false, new ExistingClientEntry("محمد أحمد علي حسن", Staff));
        await Say("الحكم طلع؟", R("existing_matter"));
        var oldIntake = S.Intake.Id;
        var (keys, d) = await Say("وعايز استشارة في موضوع ورث", R("consultation_request", matter: "inheritance"));
        Assert.Equal([TextKeys.ConsultationOffer], keys); // topic known: no question
        Assert.NotEqual(oldIntake, S.Intake.Id);
        Assert.Equal((ClientType.NewClient, "inheritance"), (S.Intake.ClientType, S.Intake.MatterType));
        Assert.Equal(IntakeState.Closed, Assert.Single(d.Effects!.State.ClosedIntakes).State);
    }

    [Fact]
    public async Task A_listed_client_asking_about_the_office_is_answered_at_once_without_the_offer()
    {
        // TASK 06 (B-20): the address does not depend on a new matter, so no «عميل قديم ولا جديد؟» first.
        _list = new ListMatch(false, false, new ExistingClientEntry("محمد أحمد علي حسن", Staff));
        Add(TextKeys.OfficeAddress, "📍 {{address}} {{map}}");
        var (none, d) = await Say("المكتب فين؟", R("office_info") with { InfoTopic = "address" });
        Assert.Equal([TextKeys.ExistingMatter], none); // nothing written to answer with: the neutral reply and their staff member
        Assert.Equal([PendingTask.OfficeInfoMissing, PendingTask.ExistingClientMessage], d.Effects!.Tasks.Select(t => t.Kind));

        _settings["office.address"] = "183 شارع التحرير";
        _settings["office.map_url"] = "https://maps.example.test/office";
        var (keys, _) = await Say("المكتب فين؟", R("office_info") with { InfoTopic = "address" });
        Assert.Equal([TextKeys.OfficeAddress], keys);
    }

    [Fact]
    public async Task An_unclear_message_from_an_existing_client_gets_the_B04_question_once()
    {
        _list = new ListMatch(false, false, new ExistingClientEntry("محمد أحمد علي حسن", Staff));
        Assert.Equal([TextKeys.ExistingOrNew], (await Say("السلام عليكم", R("greeting"))).Keys);
        Assert.DoesNotContain(TextKeys.ExistingOrNew, (await Say("ازيك", R("greeting"))).Keys);
    }

    [Fact]
    public async Task A_colleague_is_recognised_from_their_words_and_gets_a_task()
    {
        Activate(TextKeys.ColleagueReceived);
        // v1.32: recognised on the first message (no name question before it any more), never the welcome
        var (keys, d) = await Say("السلام عليكم أنا محامي", R("greeting", role: "lawyer_colleague"));
        Assert.Equal([TextKeys.ColleagueReceived], keys);
        Assert.Equal(ClientType.Colleague, S.Intake.ClientType);
        Assert.Equal(PendingTask.ExternalContact, Assert.Single(d.Effects!.Tasks).Kind);

        // Later messages wait for the staff member who has the task: no repeated reply, no new task.
        var (named, d1) = await Say("محمد أحمد علي حسن", new("provide_name", null, "unknown", "محمد أحمد علي حسن", 0.95));
        Assert.Empty(named);
        Assert.Empty(d1.Effects!.Tasks);
        var (again, d2) = await Say("بخصوص القضية المشتركة", R("existing_matter", role: "lawyer_colleague"));
        Assert.Empty(again);
        Assert.Empty(d2.Effects!.Tasks);
    }

    [Fact]
    public async Task An_urgent_message_from_a_listed_client_goes_to_their_staff_not_the_new_client_price()
    {
        _list = new ListMatch(false, false, new ExistingClientEntry("محمد أحمد علي حسن", Staff));
        var intake = (await Say("السلام عليكم", R("greeting"))).D.Effects!.State.Intake.Id;
        var (keys, d) = await Say("الجلسة بكرة الحقوني", R("urgent", matter: "criminal"));
        Assert.Equal([TextKeys.ExistingMatter], keys);
        Assert.DoesNotContain(TextKeys.UrgentPrice, keys);
        var task = Assert.Single(d.Effects!.Tasks);
        Assert.Equal((PendingTask.UrgentRequest, (Guid?)Staff), (task.Kind, task.StaffId));
        Assert.Equal((intake, ClientType.ExistingClient), (S.Intake.Id, S.Intake.ClientType));
        Assert.Empty(d.Effects.State.ClosedIntakes);
    }

    // ---------- B-13 source ----------

    [Fact]
    public async Task An_ad_referral_is_recorded_once_and_never_asked()
    {
        await Say("السلام عليكم", R("greeting"), new Referral("ad", "120000", "استشارة قانونية"));
        Assert.Equal("ad", S.Person.Source);
        Assert.Contains("120000", S.Person.SourceRaw);
        var first = S.Person.FirstSeenAt;
        await Say("محمد أحمد علي حسن", new("provide_name", null, "unknown", "محمد أحمد علي حسن", 0.95), new Referral("post", "999", null));
        Assert.Equal(("ad", first), (S.Person.Source, S.Person.FirstSeenAt));
    }

    [Fact]
    public async Task A_link_code_in_the_first_message_is_a_link_source_otherwise_direct()
    {
        _settings[HazemResponder.LinkCodesKey] = "FB-OCT, TT1";
        await Say("مرحبا FB-OCT عايز استشارة", R("consultation_request"));
        Assert.Equal("link", S.Person.Source);
        Assert.Contains("FB-OCT", S.Person.SourceRaw);
    }

    [Fact]
    public async Task No_referral_and_no_code_is_direct()
    {
        await Say("السلام عليكم", R("greeting"));
        Assert.Equal(("direct", (string?)null), (S.Person.Source, S.Person.SourceRaw));
    }

    // ---------- B-25 topic ----------

    [Fact]
    public async Task Unknown_topic_the_offer_carries_the_question_once()
    {
        var (keys, _) = await Say("السلام عليكم عايز احجز", R("consultation_request"));
        Assert.Equal([TextKeys.ConsultationOffer, TextKeys.AskTopic], keys); // v1.32: answered at once, no name first
        Assert.Equal([TextKeys.ConsultationOffer], (await Say("عايز احجز", R("consultation_request"))).Keys); // never twice
    }

    [Fact]
    public async Task After_the_services_list_the_told_topic_gets_the_offer() // v1.30: the list came, not the offer
    {
        await NewClientWithName();
        var (keys, _) = await Say("مراتي عايزة خلع وواخدة العيال", R("describe_matter", "personal_status", "khula"));
        Assert.Equal([TextKeys.ConsultationOffer], keys); // the topic is known: no topic question
        Assert.Equal(("personal_status", "khula"), (S.Intake.MatterType, S.Intake.MatterSubtype));
    }

    [Fact]
    public async Task A_topic_told_before_any_question_gets_the_offer_without_the_question()
    {
        await Say("السلام عليكم، مضيت على إيصال أمانة وبيهددني يرفع قضية", R("describe_matter", "cheques_trust_receipts"));
        var (keys, _) = await Say("محمد أحمد علي حسن", new("provide_name", null, "unknown", "محمد أحمد علي حسن", 0.95));
        Assert.Equal([TextKeys.Greeting], keys); // v1.32: the offer went with the first message; the name alone, the short reply
        Assert.Equal("cheques_trust_receipts", S.Intake.MatterType);
    }

    [Fact]
    public async Task The_urgent_path_never_asks_the_topic()
    {
        await Say("السلام عليكم", R("greeting"));
        var (keys, _) = await Say("محمد أحمد علي حسن مستعجل جدا", new("urgent", null, "unknown", "محمد أحمد علي حسن", 0.95));
        Assert.Equal([TextKeys.UrgentPrice, TextKeys.ConsultationOffer], keys);
        Assert.False(S.Intake.MatterAsked);
    }

    [Fact]
    public async Task An_unclear_topic_never_overwrites_a_known_one()
    {
        await NewClientWithName();
        await Say("موضوع ورث", R("describe_matter", "inheritance"));
        await Say("مش عارف", R("other", "unclear"));
        Assert.Equal("inheritance", S.Intake.MatterType);
    }

    // ---------- S-01 abuse ----------

    [Fact]
    public async Task Abuse_is_warned_once_then_hazem_is_silent_for_good()
    {
        Activate(TextKeys.AbuseWarning);
        await NewClientWithName();
        var (first, d1) = await Say("شتيمة", R("abusive"));
        Assert.Equal([TextKeys.AbuseWarning], first);
        Assert.Equal(PendingTask.Complaint, Assert.Single(d1.Effects!.Tasks).Kind);
        Assert.Empty((await Say("شتيمة تاني", R("abusive"))).Keys);
        Assert.Equal(ConversationState.Muted, S.Conversation.State);
        Assert.Empty((await Say("عايز احجز", R("consultation_request"))).Keys);
        Assert.True(S.Conversation.Unmute(Actor.Manager).IsOk);
        Assert.NotEmpty((await Say("عايز احجز", R("consultation_request"))).Keys);
    }

    // ---------- B-12 handoff ----------

    [Fact]
    public async Task With_handoff_off_a_staff_request_stays_a_task_and_hazem_keeps_answering()
    {
        await NewClientWithName();
        var (keys, d) = await Say("عايز اكلم موظف", R("human_request"));
        Assert.Equal([TextKeys.HumanRequest], keys);
        Assert.Equal(PendingTask.HumanRequested, Assert.Single(d.Effects!.Tasks).Kind);
        Assert.Equal(ConversationState.BotActive, S.Conversation.State);
    }

    [Fact]
    public async Task With_handoff_on_hazem_goes_silent_after_handing_over()
    {
        _settings[HazemResponder.HandoffEnabledKey] = "true";
        await NewClientWithName();
        await Say("عايز اكلم موظف", R("human_request"));
        Assert.Equal(ConversationState.HumanHandoff, S.Conversation.State);
        Assert.Empty((await Say("فين الموظف", R("human_request"))).Keys);
    }

    // ---------- S-05 tasks ----------

    [Fact]
    public async Task Every_task_carries_a_B06_due_time()
    {
        await NewClientWithName();
        var before = DateTimeOffset.UtcNow;
        var (_, d) = await Say("عايز اكلم موظف", R("human_request"));
        var due = Assert.Single(d.Effects!.Tasks).DueAt;
        Assert.InRange((due - OfficeHours.DueAt(before)).TotalSeconds, 0, 5);
    }

    // ---------- fakes ----------

    private sealed class Store(Dictionary<string, ClientState> rows) : IConversationStore
    {
        public Task<ClientState?> LoadAsync(string waId, CancellationToken ct) =>
            Task.FromResult(rows.TryGetValue(waId, out var s) ? s with { ClosedIntakes = [] } : null);

        public Task SaveConversationAsync(Conversation conversation, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Lists(Func<ListMatch> current) : IClientLists
    {
        public Task<ListMatch> FindAsync(string waId, CancellationToken ct) => Task.FromResult(current());
    }

    private sealed class Texts(Dictionary<string, ApprovedText> texts) : IApprovedTexts
    {
        public Task<IReadOnlyDictionary<string, ApprovedText>> GetActiveAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, ApprovedText>>(texts.Where(kv => kv.Value.Active).ToDictionary());
    }

    /// <summary>Answers what the test scripted for the exact text, else the keyword fake of HazemFlowTests.</summary>
    private sealed class Scripted(Dictionary<string, RawClassification> script) : IIntentClassifier
    {
        public Task<ClassifierOutcome> ClassifyAsync(string text, HazemSettings settings, CancellationToken ct) =>
            Task.FromResult(new ClassifierOutcome(
                script.TryGetValue(text, out var r) ? r : HazemFlowTests.FakeClassifier.Classify(text), null, "fake", "classify-v4", 1));
    }
}

/// <summary>B-06 due times on both sides of every boundary (Africa/Cairo), and phone numbers from the office's files.</summary>
public sealed class OfficeHoursAndPhonesTests
{
    private static DateTimeOffset Cairo(int h, int m) =>
        new(new DateTime(2026, 11, 20, h, m, 0), OfficeHours.Cairo.GetUtcOffset(new DateTime(2026, 11, 20, h, m, 0)));

    [Theory]
    [InlineData(10, 0, "within_hour")]
    [InlineData(22, 59, "within_hour")]
    [InlineData(23, 0, "tomorrow_10")]
    [InlineData(23, 59, "tomorrow_10")]
    [InlineData(0, 0, "today_10")]
    [InlineData(9, 59, "today_10")]
    public void Due_time_follows_B06(int h, int m, string expected)
    {
        var now = Cairo(h, m);
        var due = TimeZoneInfo.ConvertTime(OfficeHours.DueAt(now), OfficeHours.Cairo);
        var actual = due == TimeZoneInfo.ConvertTime(now.AddHours(1), OfficeHours.Cairo) ? "within_hour"
            : due.Date == now.Date.AddDays(1) && due.TimeOfDay == TimeSpan.FromHours(10) ? "tomorrow_10"
            : due.Date == now.Date && due.TimeOfDay == TimeSpan.FromHours(10) ? "today_10"
            : due.ToString("O");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("01012345678", "201012345678")]
    [InlineData("010 1234 5678", "201012345678")]
    [InlineData("+20 10 1234 5678", "201012345678")]
    [InlineData("0020-10-1234-5678", "201012345678")]
    [InlineData("٠١٠١٢٣٤٥٦٧٨", "201012345678")]
    [InlineData("1012345678", "201012345678")]
    [InlineData("966501234567", "966501234567")]
    public void Phones_become_wa_ids(string written, string waId)
    {
        Assert.True(PhoneNumbers.TryToWaId(written, out var got));
        Assert.Equal(waId, got);
    }

    [Theory]
    [InlineData("")]
    [InlineData("محمد")]
    [InlineData("123")]
    [InlineData("0101234567x")]
    public void Not_phone_numbers_are_refused(string written) => Assert.False(PhoneNumbers.TryToWaId(written, out _));
}
