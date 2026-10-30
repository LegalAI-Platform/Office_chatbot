using Office.Application.Conversations;
using Office.Application.Ratings;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Domain.Clients;

namespace Office.UnitTests;

/// <summary>v1.31 (DECISIONS B-01.7, S-02): when a known person's open request is finished. Pure rules.</summary>
public sealed class ReturningClientRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private static IntakeFacts Facts(TimeSpan? silent, params RequestItem[] items) => new(silent, items);

    private static RequestItem Appointment(string state, bool mine = true, DateTimeOffset? start = null) =>
        new(RequestItem.Appointment, state, mine, start ?? Now.AddDays(-1));

    [Theory]
    [InlineData("completed")]
    [InlineData("no_show")]
    [InlineData("cancelled_by_client")]
    [InlineData("cancelled_by_office")]
    [InlineData("expired")]
    public void An_appointment_that_ended_finishes_the_request_at_once(string state) =>
        Assert.True(ReturningClient.Finished(Facts(TimeSpan.FromMinutes(5), Appointment(state)), Now, Week));

    [Theory]
    [InlineData("delivered")]
    [InlineData("closed")]
    public void A_delivered_or_closed_order_finishes_it(string state) =>
        Assert.True(ReturningClient.Finished(Facts(TimeSpan.FromHours(1), new RequestItem(RequestItem.Order, state, true)), Now, Week));

    [Fact]
    public void An_urfi_case_finishes_when_the_lawyer_refused_it_or_its_appointment_is_completed()
    {
        Assert.True(ReturningClient.Finished(Facts(TimeSpan.Zero, new RequestItem(RequestItem.Urf, "lawyer_rejected", true)), Now, Week));
        Assert.True(ReturningClient.Finished(Facts(TimeSpan.Zero, new RequestItem(RequestItem.Urf, "approved", true), Appointment("completed")), Now, Week));
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.Zero, new RequestItem(RequestItem.Urf, "approved", true),
            Appointment("confirmed", start: Now.AddDays(1))), Now, Week));
        // Hazem's own refusals (age, guardian) do not: the B-11.1 closing text answers the same matter for 24 hours.
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.Zero, new RequestItem(RequestItem.Urf, "rejected_age", true)), Now, Week));
    }

    [Fact]
    public void Anything_still_open_keeps_the_request()
    {
        var ended = Appointment("completed");
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.Zero, ended, Appointment("pending_payment", mine: false, start: Now.AddDays(2))), Now, Week));
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.Zero, ended, Appointment("confirmed", mine: false, start: Now.AddDays(-2))), Now, Week)); // not marked yet
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.Zero, ended, new RequestItem(RequestItem.Order, "collecting", false)), Now, Week));
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.Zero, ended, new RequestItem(RequestItem.Payment, "proof_received", false)), Now, Week));
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.Zero, ended, new RequestItem(RequestItem.Urf, "documents_pending", false)), Now, Week));
    }

    [Fact]
    public void Only_work_of_the_open_request_finishes_it()
    {
        // An old appointment of an earlier request: the request opened after it has nothing ended yet.
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.FromDays(1), Appointment("completed", mine: false)), Now, Week));
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.FromDays(1)), Now, Week));
    }

    [Fact]
    public void Seven_silent_days_with_nothing_pending_finish_it()
    {
        Assert.True(ReturningClient.Finished(Facts(TimeSpan.FromDays(7)), Now, Week));
        Assert.False(ReturningClient.Finished(Facts(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1)), Now, Week));
        Assert.False(ReturningClient.Finished(Facts(null), Now, Week)); // never wrote before
        // An order left waiting for documents, or a confirmed appointment in the past nobody marked: not pending.
        Assert.True(ReturningClient.Finished(Facts(TimeSpan.FromDays(8), new RequestItem(RequestItem.Order, "collecting", true),
            Appointment("confirmed", start: Now.AddDays(-9))), Now, Week));
        // A future or held appointment, a priced or running order, a receipt under review, a running Urfi case: pending.
        foreach (var pending in new[]
                 {
                     Appointment("confirmed", start: Now.AddDays(1)), Appointment("pending_payment"),
                     new RequestItem(RequestItem.Order, "awaiting_price", true), new RequestItem(RequestItem.Order, "awaiting_payment", true),
                     new RequestItem(RequestItem.Order, "in_progress", true), new RequestItem(RequestItem.Payment, "duplicate_suspected", false),
                     new RequestItem(RequestItem.Urf, "collecting", true),
                 })
        {
            Assert.False(ReturningClient.Finished(Facts(TimeSpan.FromDays(30), pending), Now, Week));
        }
    }

    [Fact]
    public void The_quiet_days_and_the_first_name_come_from_the_settings_and_the_stored_name()
    {
        Assert.Equal(TimeSpan.FromDays(7), ReturningClient.Quiet(new HazemSettings(new Dictionary<string, string>())));
        Assert.Equal(TimeSpan.FromDays(3), ReturningClient.Quiet(new HazemSettings(new Dictionary<string, string> { [ReturningClient.FreshAfterDaysKey] = "3" })));
        Assert.Equal(TimeSpan.FromDays(7), ReturningClient.Quiet(new HazemSettings(new Dictionary<string, string> { [ReturningClient.FreshAfterDaysKey] = "0" })));

        var person = Person.New(Office.Domain.Common.WaId.From("201000000077"));
        Assert.Null(ReturningClient.FirstName(person));
        Assert.True(Office.Domain.Common.PersonName.TryParse("  محمد   أحمد علي", out var name, minWords: 1));
        person.GiveName(name);
        Assert.Equal("محمد", ReturningClient.FirstName(person));
    }
}

/// <summary>v1.31 (DECISIONS B-26): the rating's timing, who gets it, its rows and what it says. Pure rules.</summary>
public sealed class RatingRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly RatingSettings S = RatingSettings.Default;

    [Fact]
    public void Two_hours_after_a_consultation_and_24_hours_after_a_delivery()
    {
        Assert.Equal(Now.AddHours(2), RatingRules.Due("consultation_office", Now, S));
        Assert.Equal(Now.AddHours(2), RatingRules.Due("urf_marriage_own", Now, S));
        Assert.Equal(Now.AddHours(24), RatingRules.Due("document_review", Now, S));
        Assert.Equal(Now.AddHours(5), RatingRules.Due("consultation_phone_video", Now, S with { AfterAppointment = TimeSpan.FromHours(5) }));
    }

    [Fact]
    public void Never_the_urgent_consultation_nor_an_urfi_divorce()
    {
        Assert.True(RatingRules.IsRated("consultation_phone_video"));
        Assert.True(RatingRules.IsRated("urf_marriage_office"));
        Assert.True(RatingRules.IsRated("written_opinion"));
        Assert.False(RatingRules.IsRated("consultation_urgent"));
        Assert.False(RatingRules.IsRated("urf_divorce_office"));
        Assert.False(RatingRules.IsRated("urf_divorce_own"));
    }

    [Fact]
    public void Off_muted_recent_and_late_are_skipped_in_that_order()
    {
        var due = Now.AddHours(-1);
        Assert.Null(RatingRules.Skip(S, true, false, null, due, Now));
        Assert.Equal(RatingSkips.Off, RatingRules.Skip(S with { Enabled = false }, true, true, Now, due, Now));
        Assert.Equal(RatingSkips.Off, RatingRules.Skip(S, false, false, null, due, Now)); // a text switched off
        Assert.Equal(RatingSkips.Muted, RatingRules.Skip(S, true, true, Now, due, Now));
        Assert.Equal(RatingSkips.Recent, RatingRules.Skip(S, true, false, Now.AddDays(-29), due, Now));
        Assert.Null(RatingRules.Skip(S, true, false, Now.AddDays(-30), due, Now));
        Assert.Equal(RatingSkips.Late, RatingRules.Skip(S, true, false, null, Now.AddDays(-2), Now));
    }

    [Fact]
    public void The_settings_default_to_the_owners_numbers()
    {
        var s = RatingSettings.From(new HazemSettings(new Dictionary<string, string>()));
        Assert.Equal((true, TimeSpan.FromHours(2), TimeSpan.FromHours(24), TimeSpan.FromDays(30), (DateTimeOffset?)null),
            (s.Enabled, s.AfterAppointment, s.AfterDelivery, s.Repeat, s.Since));
        var set = RatingSettings.From(new HazemSettings(new Dictionary<string, string>
        {
            [RatingSettings.EnabledKey] = "false", [RatingSettings.RepeatKey] = "10", [RatingSettings.SinceKey] = "2026-10-30T10:00:00Z",
            [RatingSettings.AfterDeliveryKey] = "-3",
        }));
        Assert.Equal((false, TimeSpan.FromDays(10), TimeSpan.FromHours(24)), (set.Enabled, set.Repeat, set.AfterDelivery));
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.Zero), set.Since);
    }

    [Fact]
    public void The_rows_are_read_by_code_and_name_their_rating()
    {
        var id = Guid.NewGuid();
        Assert.Equal(new RatingPress(id, 5), RatingButtons.Parse(RatingButtons.Id(id, 5)));
        Assert.Equal(new RatingPress(id, null), RatingButtons.Parse(RatingButtons.Id(id, null)));
        Assert.True(RatingButtons.Parse(RatingButtons.Id(id, 2))!.IsLow);
        Assert.Null(RatingButtons.Parse($"rt:{id:N}:1"));
        Assert.Null(RatingButtons.Parse($"rt:{id:N}:6"));
        Assert.Null(RatingButtons.Parse("rt:x:5"));
        Assert.Null(RatingButtons.Parse($"sv:{id:N}:5"));
        Assert.Equal([5, 4, 3, 2, (int?)null], RatingTexts.Rows.Select(r => r.Score));
        Assert.All(RatingTexts.Rows, r => Assert.NotNull(RatingButtons.Parse(RatingButtons.Id(id, r.Score))));
    }

    [Fact]
    public void Each_answer_has_its_reply_and_each_service_its_name()
    {
        Assert.Equal(TextKeys.RatingThanks, RatingTexts.Reply(5));
        Assert.Equal(TextKeys.RatingThanks, RatingTexts.Reply(3));
        Assert.Equal(TextKeys.RatingLow, RatingTexts.Reply(2));
        Assert.Equal(TextKeys.RatingLater, RatingTexts.Reply(null));
        Assert.Equal("الاستشارة", RatingRules.ServiceName("consultation_office"));
        Assert.Equal("الاستشارة", RatingRules.ServiceName("consultation_phone_video"));
        Assert.Equal("عقد الجواز العرفي", RatingRules.ServiceName("urf_marriage_own"));
        Assert.Equal("مراجعة العقد", RatingRules.ServiceName("document_review"));
        Assert.Equal("service_rating", RatingTexts.Template);
    }
}

/// <summary>v1.31 through Hazem: the returning client's welcome and the rating's answers (fake stores, no database).</summary>
public sealed class RatingAndReturningFlowTests
{
    private const string Number = "201000000131";
    private readonly Dictionary<string, ClientState> _rows = [];
    private readonly HazemFlowTests.FakeClassifier _ai = new();
    private readonly Dictionary<string, ApprovedText> _texts = HazemFlowTests.Approved.ToDictionary(kv => kv.Key, kv => new ApprovedText(kv.Key, 1, kv.Value, 300, true));
    private readonly History _history = new();
    private readonly Ratings _ratings = new();
    private int _turn;

    public RatingAndReturningFlowTests()
    {
        foreach (var (k, v) in new Dictionary<string, string>
                 {
                     [TextKeys.WelcomeBack] = "أهلًا بحضرتك تاني يا {{first_name}} 👋",
                     [TextKeys.ServicesMenu] = "T:services_menu", [TextKeys.BtnServicesList] = "خدمات المكتب",
                     [TextKeys.RatingThanks] = "شكرًا لحضرتك على التقييم 🙏 تحت أمرك في أي وقت.",
                     [TextKeys.RatingLow] = "T:rating_low", [TextKeys.RatingLater] = "تمام، تحت أمرك في أي وقت.",
                     [TextKeys.Thanks] = "T:thanks", [TextKeys.Greeting] = "T:greeting",
                 })
        {
            _texts[k] = new ApprovedText(k, 1, v, 300, true);
        }

        foreach (var k in new[] { TextKeys.RowUrgent, TextKeys.RowConsultPhone, TextKeys.RowCase, TextKeys.RowReview, TextKeys.RowDrafting,
                     TextKeys.RowNotice, TextKeys.RowOpinion, TextKeys.RowUrf, TextKeys.RowTell })
        {
            _texts[k] = new ApprovedText(k, 1, "row " + k[4..], 24, true);
        }
    }

    private HazemResponder Hazem => new(new Store(_rows), _ai, new Texts(_texts), new NoLists(), TimeProvider.System,
        history: _history, ratings: _ratings);

    private async Task<TurnDecision> Send(IncomingMessage m)
    {
        var d = await Hazem.DecideAsync(new Turn($"wt:{++_turn}", Number, null, [m]),
            new HazemSettings(new Dictionary<string, string> { [HazemResponder.RecipientsKey] = Number }), CancellationToken.None);
        if (d.Effects is { } e)
        {
            _rows[Number] = e.State;
        }

        return d;
    }

    private Task<TurnDecision> Say(string text) => Send(new IncomingMessage($"wamid.{++_turn}", MessageType.Text, text, null, null));

    private Task<TurnDecision> Press(string id) =>
        Send(new IncomingMessage($"wamid.{++_turn}", MessageType.Interactive, null, null, null, Interactive: new Interactive("list_reply", id, "x")));

    private static List<string> Keys(TurnDecision d) => d.Effects!.Replies.Select(r => r.Key).ToList();

    private static List<string> Tasks(TurnDecision d) => d.Effects!.Tasks.Select(t => t.Kind).ToList();

    private async Task Known()
    {
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن"); // v1.32: after the welcome, the name told on its own: the services list
    }

    // ---------- B-01.7 ----------

    [Fact]
    public async Task A_returning_client_gets_welcome_back_with_his_first_name_then_the_two_buttons()
    {
        await Known();
        var before = _rows[Number].Intake.Id;
        _history.Facts = new IntakeFacts(TimeSpan.FromDays(1),
            [new RequestItem(RequestItem.Appointment, "completed", true, DateTimeOffset.UtcNow.AddDays(-1))]);

        var d = await Say("صباح الخير");
        Assert.Equal([TextKeys.WelcomeBack, TextKeys.WelcomeChoose], Keys(d)); // v1.32: the welcome's question alone
        Assert.Equal("أهلًا بحضرتك تاني يا محمد 👋", d.Actions[0].Text); // its own message, before the question
        Assert.Equal(InteractivePayload.Buttons, d.Actions[1].Interactive!.Kind);
        Assert.Equal(["sv:menu", "sv:tell"], d.Actions[1].Interactive!.Choices.Select(c => c.Id));
        var s = d.Effects!.State;
        Assert.NotEqual(before, s.Intake.Id); // a new request for the same person, the old one closed (nothing deleted)
        Assert.Equal(IntakeState.Closed, Assert.Single(s.ClosedIntakes).State);
        Assert.Equal((ClientType.NewClient, IntakeState.OfferSent), (s.Intake.ClientType!.Value, s.Intake.State));
        Assert.Equal(before, _history.AskedFor);
        Assert.DoesNotContain(TextKeys.AskNameBooking, Keys(d)); // no name question
    }

    [Fact]
    public async Task A_returning_client_who_never_gave_his_name_gets_the_welcome_of_a_new_one() // v1.32
    {
        await Say("السلام عليكم"); // the welcome; no name asked
        _history.Facts = new IntakeFacts(TimeSpan.FromDays(8), []);
        var d = await Say("صباح الخير");
        Assert.Equal([TextKeys.Welcome], Keys(d)); // no «أهلًا بحضرتك تاني يا …» without a first name
        Assert.Equal(["sv:menu", "sv:tell"], d.Actions[0].Interactive!.Choices.Select(c => c.Id));
        Assert.Single(d.Effects!.State.ClosedIntakes);
    }

    [Fact]
    public async Task A_returning_client_who_says_his_request_gets_it_after_the_welcome_and_a_thanks_gets_its_reply_only()
    {
        await Known();
        _history.Facts = new IntakeFacts(TimeSpan.FromDays(8), []); // seven silent days, nothing pending
        Assert.Equal([TextKeys.WelcomeBack, TextKeys.ConsultationOffer], Keys(await Say("عايز أحجز استشارة")));

        _history.Facts = new IntakeFacts(TimeSpan.FromDays(8), []);
        Assert.Equal([TextKeys.Thanks], Keys(await Say("شكرا")));
    }

    [Fact]
    public async Task A_request_that_is_not_finished_goes_on_without_the_welcome()
    {
        await Known();
        var before = _rows[Number].Intake.Id;
        _history.Facts = new IntakeFacts(TimeSpan.FromDays(1),
            [new RequestItem(RequestItem.Appointment, "confirmed", true, DateTimeOffset.UtcNow.AddDays(1))]);
        var d = await Say("عايز أحجز استشارة");
        Assert.DoesNotContain(TextKeys.WelcomeBack, Keys(d));
        Assert.Equal(before, d.Effects!.State.Intake.Id);
        Assert.Empty(d.Effects.State.ClosedIntakes);
    }

    [Fact]
    public async Task A_muted_conversation_stays_silent()
    {
        await Known();
        _rows[Number].Conversation.Mute(Office.Domain.Common.Actor.System);
        _history.AskedFor = null; // v1.32: Known()'s second message was already checked (the name is no longer needed for it)
        _history.Facts = new IntakeFacts(TimeSpan.FromDays(30), []);
        var d = await Say("صباح الخير");
        Assert.Empty(d.Actions);
        Assert.Null(_history.AskedFor);
    }

    // ---------- B-26 ----------

    [Theory]
    [InlineData(5, TextKeys.RatingThanks)]
    [InlineData(4, TextKeys.RatingThanks)]
    [InlineData(3, TextKeys.RatingThanks)]
    [InlineData(null, TextKeys.RatingLater)]
    public async Task A_rating_row_is_stored_and_answered_by_code(int? score, string reply)
    {
        await Known();
        var id = Guid.NewGuid();
        var calls = _ai.Texts.Count;
        var d = await Press(RatingButtons.Id(id, score));
        Assert.Equal([reply], Keys(d));
        Assert.Empty(Tasks(d));
        Assert.Equal(new RatingPress(id, score), Assert.Single(_ratings.Presses));
        Assert.Equal(calls, _ai.Texts.Count); // never the AI
    }

    [Fact]
    public async Task Needs_improvement_is_a_task_and_the_next_words_are_its_reason_with_no_reply()
    {
        await Known();
        var d = await Press(RatingButtons.Id(Guid.NewGuid(), 2));
        Assert.Equal([TextKeys.RatingLow], Keys(d));
        Assert.Equal([PendingTask.RatingLow], Tasks(d));

        _ratings.ReasonTaken = true;
        var calls = _ai.Texts.Count;
        var reason = await Say("الأستاذ اتأخر عليا نص ساعة");
        Assert.Empty(reason.Actions); // the office calls, as rating_low told him
        Assert.Empty(Tasks(reason));
        Assert.Equal(calls, _ai.Texts.Count);
        Assert.Equal(reason.Effects!.State.Intake.Id, _rows[Number].Intake.Id);

        _ratings.ReasonTaken = false; // any later message is a normal one
        Assert.NotEmpty((await Say("عايز أحجز استشارة")).Actions);
    }

    [Fact]
    public async Task A_second_press_changes_nothing_and_somebody_elses_rating_gets_no_answer()
    {
        await Known();
        _ratings.Next = new RatingAnswer(RatingAnswerStatus.Again, 4);
        Assert.Equal([TextKeys.RatingThanks], Keys(await Press(RatingButtons.Id(Guid.NewGuid(), 2))));
        _ratings.Next = new RatingAnswer(RatingAnswerStatus.Again, null);
        Assert.Equal([TextKeys.RatingLater], Keys(await Press(RatingButtons.Id(Guid.NewGuid(), null))));
        _ratings.Next = new RatingAnswer(RatingAnswerStatus.Unknown, null);
        var d = await Press(RatingButtons.Id(Guid.NewGuid(), 5));
        Assert.Empty(d.Actions);
        Assert.Empty(Tasks(d));
    }

    // ---------- fakes ----------

    private sealed class History : IIntakeHistory
    {
        public IntakeFacts Facts { get; set; } = new(null, []);

        public Guid? AskedFor { get; set; }

        public Task<IntakeFacts> FactsAsync(Guid personId, string waId, Guid intakeId, CancellationToken ct)
        {
            AskedFor = intakeId;
            return Task.FromResult(Facts);
        }
    }

    private sealed class Ratings : IRatingStore
    {
        public List<RatingPress> Presses { get; } = [];

        public RatingAnswer? Next { get; set; }

        public bool ReasonTaken { get; set; }

        public Task<RatingAnswer> AnswerAsync(Guid personId, RatingPress press, string turnId, DateTimeOffset now, CancellationToken ct)
        {
            Presses.Add(press);
            return Task.FromResult(Next ?? new RatingAnswer(RatingAnswerStatus.Recorded, press.Score));
        }

        public Task<bool> TakeReasonAsync(Guid personId, string waId, string turnId, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(ReasonTaken);
    }

    private sealed class Store(Dictionary<string, ClientState> rows) : IConversationStore
    {
        public Task<ClientState?> LoadAsync(string waId, CancellationToken ct) => Task.FromResult(rows.TryGetValue(waId, out var s) ? s : null);

        public Task SaveConversationAsync(Conversation conversation, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoLists : IClientLists
    {
        public Task<ListMatch> FindAsync(string waId, CancellationToken ct) => Task.FromResult(ListMatch.None);
    }

    private sealed class Texts(Dictionary<string, ApprovedText> texts) : IApprovedTexts
    {
        public Task<IReadOnlyDictionary<string, ApprovedText>> GetActiveAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, ApprovedText>>(texts.Where(kv => kv.Value.Active).ToDictionary());
    }
}
