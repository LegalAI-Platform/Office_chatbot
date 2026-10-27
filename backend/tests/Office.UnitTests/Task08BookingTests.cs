using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Reminders;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Understanding;
using Office.Domain.Booking;
using Office.Domain.Tasks;

namespace Office.UnitTests;

/// <summary>TASK 08 (DECISIONS v1.12, B-24): the free times from the panel's hours, the exceptions and the calendar.</summary>
public sealed class AvailabilityTests
{
    private static readonly TimeZoneInfo Z = OfficeHours.Cairo;
    private static readonly BookingRules R = BookingRules.Default; // 10 min buffer, 2 h notice, 14 days, every 15 min

    /// <summary>November 2026 (no clock change): the 19th is a Thursday.</summary>
    internal static DateTimeOffset At(int day, int h, int m = 0)
    {
        var local = new DateTime(2026, 11, day, h, m, 0);
        return new DateTimeOffset(local, Z.GetUtcOffset(local));
    }

    internal static WeeklyHours Every(int start, int end, params DayOfWeek[] off) =>
        WeeklyHours.Create(Enum.GetValues<DayOfWeek>().Where(d => !off.Contains(d)).Select(d => (d, new MinuteRange(start, end)))).Hours!;

    private static DateOnly D(int day) => new(2026, 11, day);

    private static IReadOnlyList<Slot> Find(Schedule s, DateTimeOffset now, BookingRules? rules = null, int minutes = 15, bool urgent = false,
        DateOnly? only = null, int limit = 4, BusyInterval[]? booked = null, BusyInterval[]? busy = null, DateTimeOffset? after = null) =>
        Availability.Find(s, rules ?? R, new SlotQuery(minutes, after ?? now, urgent, only, limit), booked ?? [], busy ?? [], now, Z);

    private static Schedule S(params DayException[] e) => new(Every(600, 1440), e);

    [Fact]
    public void The_first_times_start_after_the_minimum_notice_on_the_step()
    {
        var slots = Find(S(), At(19, 12, 7));
        Assert.Equal([At(19, 14, 15), At(19, 14, 30), At(19, 14, 45), At(19, 15)], slots.Select(s => s.Start));
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(15), s.End - s.Start));
    }

    [Fact]
    public void A_closed_day_is_never_offered()
    {
        var slots = Find(S(new DayException(D(19), ExceptionKind.ClosedDay, null)), At(19, 9));
        Assert.Equal(At(20, 10), slots[0].Start);
    }

    [Fact]
    public void Closed_hours_and_other_hours_of_one_day()
    {
        Assert.Equal(At(19, 17), Find(S(new DayException(D(19), ExceptionKind.ClosedHours, new MinuteRange(14 * 60, 17 * 60))), At(19, 12))[0].Start);

        var custom = Find(S(new DayException(D(19), ExceptionKind.CustomHours, new MinuteRange(18 * 60, 19 * 60))), At(19, 9), limit: 10);
        Assert.Equal([At(19, 18), At(19, 18, 15), At(19, 18, 30), At(19, 18, 45), At(20, 10)], custom.Take(5).Select(s => s.Start));
    }

    [Fact]
    public void Calendar_events_and_appointments_are_kept_clear_with_the_buffer()
    {
        Assert.Equal(At(19, 15, 15), Find(S(), At(19, 12), busy: [new BusyInterval(At(19, 14), At(19, 15))])[0].Start);
        Assert.Equal(At(19, 14, 45), Find(S(), At(19, 12), booked: [new BusyInterval(At(19, 14), At(19, 14, 30))])[0].Start);
    }

    [Fact]
    public void A_full_day_and_the_horizon()
    {
        var rules = R with { MaxPerDay = 1 };
        Assert.Equal(At(20, 10), Find(S(), At(19, 9), rules, booked: [new BusyInterval(At(19, 20), At(19, 20, 15))])[0].Start);

        var all = Find(S(), At(19, 23), R with { HorizonDays = 1 }, limit: 500);
        Assert.All(all, s => Assert.Equal(20, TimeZoneInfo.ConvertTime(s.Start, Z).Day));
        Assert.Equal(56, all.Count); // 10:00–24:00 every 15 minutes on the 20th, nothing after the horizon
    }

    [Fact]
    public void Urgent_has_no_notice_and_only_its_day()
    {
        var slots = Find(S(), At(19, 12, 3), urgent: true, only: D(19));
        Assert.Equal(At(19, 12, 15), slots[0].Start);
        Assert.Empty(Find(S(), At(19, 23, 50), urgent: true, only: D(19)));
    }

    [Fact]
    public void A_weekly_day_off_and_paging_on()
    {
        var friday = new Schedule(Every(600, 1440, DayOfWeek.Friday), []);
        Assert.Equal(At(21, 10), Find(friday, At(19, 23, 30))[0].Start);
        Assert.Equal(At(19, 15, 15), Find(S(), At(19, 12), after: At(19, 15))[0].Start); // «مواعيد تانية»
    }

    [Fact]
    public void The_week_refuses_overlapping_or_impossible_ranges()
    {
        Assert.Equal("overlapping_ranges", WeeklyHours.Create([(DayOfWeek.Monday, new MinuteRange(600, 900)), (DayOfWeek.Monday, new MinuteRange(800, 1000))]).Problem);
        Assert.Equal("invalid_range", WeeklyHours.Create([(DayOfWeek.Monday, new MinuteRange(900, 600))]).Problem);
        Assert.Equal("invalid_range", WeeklyHours.Create([(DayOfWeek.Monday, new MinuteRange(600, 1500))]).Problem);
        Assert.NotNull(WeeklyHours.Create([(DayOfWeek.Monday, new MinuteRange(600, 840)), (DayOfWeek.Monday, new MinuteRange(1080, 1440))]).Hours);
    }

    [Fact]
    public void One_exact_time_is_checked_again_before_booking()
    {
        Assert.True(Availability.IsFree(S(), R, At(19, 14), 15, false, [], [], At(19, 12), Z));
        Assert.False(Availability.IsFree(S(), R, At(19, 14, 5), 15, false, [], [], At(19, 12), Z)); // not on the step
        Assert.False(Availability.IsFree(S(), R, At(19, 14), 15, false, [], [new BusyInterval(At(19, 14), At(19, 15))], At(19, 12), Z));
        Assert.False(Availability.IsFree(S(), R, At(19, 13), 15, false, [], [], At(19, 12), Z)); // inside the notice
    }

    [Theory]
    [InlineData(-1, 2, 14, 15, 4, "buffer_minutes")]
    [InlineData(10, 2, 0, 15, 4, "horizon_days")]
    [InlineData(10, 2, 14, 7, 4, "slot_step_minutes")]
    [InlineData(10, 2, 14, 15, 12, "offer_count")]
    [InlineData(10, 2, 14, 15, 4, null)]
    public void Rules_out_of_range_are_refused(int buffer, int notice, int horizon, int step, int count, string? problem) =>
        Assert.Equal(problem, new BookingRules(buffer, notice, horizon, null, step, count).Problem());

    [Fact]
    public void The_status_of_a_day()
    {
        var s = new Schedule(Every(600, 1440, DayOfWeek.Friday),
        [
            new DayException(D(23), ExceptionKind.ClosedDay, null),
            new DayException(D(24), ExceptionKind.ClosedHours, new MinuteRange(600, 700)),
            new DayException(D(25), ExceptionKind.CustomHours, new MinuteRange(600, 700)),
        ]);
        Assert.Equal(DayStatus.WeeklyOff, s.StatusOn(D(20)));
        Assert.Equal(DayStatus.Open, s.StatusOn(D(21)));
        Assert.Equal(DayStatus.Closed, s.StatusOn(D(23)));
        Assert.Equal(DayStatus.PartlyClosed, s.StatusOn(D(24)));
        Assert.Equal(DayStatus.CustomHours, s.StatusOn(D(25)));
        Assert.Equal([new MinuteRange(700, 1440)], s.HoursOn(D(24)));
    }

    [Fact]
    public void The_spring_change_of_the_clock_skips_the_hour_that_does_not_exist()
    {
        // Egypt moves the clock from 00:00 to 01:00 on the last Friday of April (2026-04-24).
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 4, 24),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 23, 0, 0), 10, 29));
        var zone = TimeZoneInfo.CreateCustomTimeZone("cairo-test", TimeSpan.FromHours(2), "cairo-test", "EET", "EEST", [rule]);
        var day = new DateOnly(2026, 4, 24);
        var schedule = new Schedule(Every(0, 1440), [new DayException(day, ExceptionKind.CustomHours, new MinuteRange(0, 120))]);
        var now = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.FromHours(2));
        var slots = Availability.Find(schedule, R with { MinNoticeHours = 0 }, new SlotQuery(15, now, false, day, 10), [], [], now, zone);
        Assert.Equal(new DateTimeOffset(2026, 4, 24, 1, 0, 0, TimeSpan.FromHours(3)), slots[0].Start);
        Assert.Equal(4, slots.Count); // 01:00, 01:15, 01:30, 01:45: never a time in the missing hour
    }
}

/// <summary>The ids in the buttons, and the buttons and lists Hazem composes (TASK 08).</summary>
public sealed class BookingChoiceAndComposerTests
{
    [Fact]
    public void Ids_go_and_come_back_and_anything_else_is_ignored()
    {
        var at = AvailabilityTests.At(19, 14);
        var id = Guid.NewGuid();
        Assert.Equal(('s', "consultation_office", at), Parse(BookingChoice.Pick(BookingService.Office, at)));
        Assert.Equal(('m', "consultation_phone_video", (DateTimeOffset?)null), Parse(BookingChoice.Mode(BookingService.PhoneVideo)));
        Assert.Equal(id, BookingChoice.Parse(BookingChoice.Cancel(id))!.AppointmentId);
        Assert.Equal(id, BookingChoice.Parse(BookingChoice.MoveTo(id, at))!.AppointmentId);
        Assert.True(BookingChoice.MoreMoves(id, at).Length <= 200); // WhatsApp's limit for an id
        foreach (var junk in new[] { null, "", "hello", "bk:", "bk:s:pv", "bk:s:zz:1793000000", "bk:s:pv:12", "bk:c:not-a-guid", "bk:x:pv", "bk:ss:pv:1793000000" })
        {
            Assert.Null(BookingChoice.Parse(junk));
        }

        static (char, string?, DateTimeOffset?) Parse(string s) => BookingChoice.Parse(s) is { } c ? (c.Kind, c.Service?.Code, c.At) : default;
    }

    private static Dictionary<string, ApprovedText> Texts(params (string Key, string Text, int Max)[] t) =>
        t.ToDictionary(x => x.Key, x => new ApprovedText(x.Key, 1, x.Text, x.Max, true));

    [Fact]
    public void Buttons_and_a_list_go_out_as_one_interactive_message()
    {
        var texts = Texts(("body", "اختار", 300), ("b1", "زرار", 20), ("btn", "المواعيد", 20), ("more", "مواعيد تانية", 24));
        var buttons = ReplyComposer.Compose("t", "wamid.1",
            [new ReplyStep("body", null, null, new ReplyChoices(InteractivePayload.Buttons, [new ReplyChoice("bk:m:pv", "b1")]))], texts);
        var a = Assert.Single(buttons.Actions);
        Assert.Equal((ActionType.SendInteractive, "اختار", "wamid.1"), (a.Type, a.Text, a.ContextWamid));
        Assert.Equal(("bk:m:pv", "زرار"), (a.Interactive!.Choices[0].Id, a.Interactive.Choices[0].Title));

        var list = ReplyComposer.Compose("t", null,
            [new ReplyStep("body", null, null, new ReplyChoices(InteractivePayload.List, [new ReplyChoice("r1", Title: "النهارده 2:00 م"), new ReplyChoice("r2", "more")], "btn"))], texts);
        var l = Assert.Single(list.Actions).Interactive!;
        Assert.Equal((InteractivePayload.List, "المواعيد", "مواعيد تانية"), (l.Kind, l.Button, l.Choices[1].Title));
    }

    [Fact]
    public void A_title_not_approved_or_too_long_sends_the_text_alone()
    {
        var texts = Texts(("body", "اختار", 300), ("long", "عنوان طويل جدًا أطول من عشرين حرف", 40));
        var notApproved = ReplyComposer.Compose("t", null,
            [new ReplyStep("body", null, null, new ReplyChoices(InteractivePayload.Buttons, [new ReplyChoice("x", "missing")]))], texts);
        Assert.Equal(ActionType.SendText, Assert.Single(notApproved.Actions).Type);
        Assert.Contains(notApproved.Skipped, s => s.Key == "missing");

        var tooLong = ReplyComposer.Compose("t", null,
            [new ReplyStep("body", null, null, new ReplyChoices(InteractivePayload.Buttons, [new ReplyChoice("x", "long")]))], texts);
        Assert.Equal(ActionType.SendText, Assert.Single(tooLong.Actions).Type);
        Assert.Contains(tooLong.Skipped, s => s.Reason == ReplyComposer.TitleTooLong);
    }

    [Fact]
    public void Days_and_times_in_hazem_s_words()
    {
        var now = AvailabilityTests.At(19, 12);
        Assert.Equal("النهارده 2:00 م", BookingWords.Row(AvailabilityTests.At(19, 14), now));
        Assert.Equal("بكرة 10:15 ص", BookingWords.Row(AvailabilityTests.At(20, 10, 15), now));
        Assert.Equal("الأحد 22/11 12:00 م", BookingWords.Row(AvailabilityTests.At(22, 12), now));
        Assert.True(BookingWords.Row(AvailabilityTests.At(25, 12, 45), now).Length <= InteractivePayload.MaxRowTitle);
    }
}


/// <summary>
/// The booking in the conversation (B-08, B-09, B-15, B-22; v1.13: the hold until the receipt image) with an
/// in-memory store and calendar.
/// </summary>
public sealed class BookingFlowTests
{
    private const string Number = "201000000088";
    private static readonly DateTimeOffset Now = AvailabilityTests.At(19, 12);

    private readonly Dictionary<string, ClientState> _rows = [];
    private readonly Dictionary<string, ApprovedText> _texts = [];
    private readonly MemoryBooking _booking;
    private readonly FakeCalendar _calendar = new();
    private readonly Dictionary<string, RawClassification> _script = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _settings = new()
    {
        [HazemResponder.RecipientsKey] = Number,
        [BookingSettings.EnabledKey] = "true",
        [OfficeInfo.InstaPayKey] = "office@instapay",
        [OfficeInfo.WalletNumberKey] = "01000000000",
        [OfficeInfo.WalletProviderKey] = "فودافون كاش",
    };
    private int _turn;
    private DateTimeOffset _now = Now;

    public BookingFlowTests()
    {
        _booking = new MemoryBooking(() => _now);
        foreach (var (k, v) in HazemFlowTests.Approved)
        {
            _texts[k] = new ApprovedText(k, 1, v, 300, true);
        }

        foreach (var k in new[] { TextKeys.AskNameAgain, TextKeys.RescheduleOrCancel, TextKeys.AskTopic, TextKeys.Greeting })
        {
            _texts[k] = new ApprovedText(k, 1, "T:" + k, 300, true);
        }

        _texts[TextKeys.MediaReceived] = new ApprovedText(TextKeys.MediaReceived, 1, "وصلت {{media}}", 300, true);
        var withSlots = new Dictionary<string, string>
        {
            [TextKeys.BookingExisting] = "عندك {{service}} {{day}} {{time}}",
            [TextKeys.BookingRescheduled] = "اتغير {{day}} {{time}}",
            [TextKeys.BookingHold] = "⏳ {{service}} يوم {{day}} الساعة {{time}}. حوّل {{amount}} جنيه على InstaPay: {{instapay}}، أو محفظة كاش: {{wallet}}. قبل الساعة {{deadline}}.",
            [TextKeys.BookingPaid] = "✅ اتأكد {{service}} يوم {{day}} الساعة {{time}}",
            [TextKeys.BookingHoldOne] = "عندك مبدئي {{day}} {{time}} لحد {{deadline}}",
            [TextKeys.BookingHoldLate] = "اتحجز {{day}} {{time}}",
        };
        foreach (var k in HazemResponder.BookingTextKeys)
        {
            var max = k.StartsWith("btn_", StringComparison.Ordinal) ? 20 : k == TextKeys.BookingHold ? 600 : 300;
            _texts[k] = new ApprovedText(k, 1, withSlots.TryGetValue(k, out var t) ? t : "T:" + k[..Math.Min(k.Length, 16)], max, true);
        }
    }

    private HazemResponder Hazem => new(new Store(_rows), new Scripted(_script), new Texts(_texts), new NoLists(), new Clock(_now),
        new BookingDesk(_booking, _calendar), new Office.Application.Services.ServiceDesk(_booking),
        new Office.Application.Urf.UrfDesk(new MemoryUrf(_booking, () => _now)));

    private Task<TurnDecision> Send(IncomingMessage m) => Send(new Turn($"wt:{++_turn}", Number, null, [m]));

    private async Task<TurnDecision> Send(Turn turn)
    {
        var d = await Hazem.DecideAsync(turn, new HazemSettings(_settings), CancellationToken.None);
        if (d.Effects is { } e)
        {
            _rows[Number] = e.State;
            // as hazem.turn_replies: the closing texts sent after the age refusal, and any other reply since (v1.20)
            _booking.AgeClosingReplies += e.Replies.Count(r => r.Key == TextKeys.UrfAgeClosed);
            _booking.AgeMovedOn |= _booking.AgeRefusedAt is not null
                                   && e.Replies.Any(r => r.Key is not (TextKeys.UrfAgeClosed or TextKeys.UrfUnderAge));
        }

        return d;
    }

    private Task<TurnDecision> Say(string text) => Send(new IncomingMessage($"wamid.{++_turn}", MessageType.Text, text, null, null));

    /// <summary>Several messages grouped into one turn (the 8 seconds of TASK 03).</summary>
    private Task<TurnDecision> Together(params IncomingMessage[] messages) => Send(new Turn($"wt:{++_turn}", Number, null, messages));

    private IncomingMessage Typed(string text) => new($"wamid.{++_turn}", MessageType.Text, text, null, null);

    private IncomingMessage Pressed(string id) =>
        new($"wamid.{++_turn}", MessageType.Interactive, null, null, null, Interactive: new Interactive("list_reply", id, "x"));

    // ---------- v1.22 (B-08, B-25) ----------

    [Fact]
    public async Task The_office_pressed_then_a_phone_time_in_the_same_turn_offers_the_office_times_and_holds_nothing()
    {
        await ToTheOffer();
        await Press(BookingChoice.Mode(BookingService.PhoneVideo));
        var d = await Together(Pressed(BookingChoice.Mode(BookingService.Office)),
            Pressed(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(19, 14))));
        Assert.Empty(_booking.Appointments);
        var officePick = BookingChoice.Pick(BookingService.Office, AvailabilityTests.At(19, 14));
        var prefix = officePick[..(officePick.LastIndexOf(':') + 1)];
        Assert.All(Assert.Single(d.Actions).Interactive!.Choices.SkipLast(1), c => Assert.StartsWith(prefix, c.Id));
    }

    [Fact]
    public async Task Words_typed_with_a_time_record_their_topic_on_the_booking()
    {
        _script["السلام عليكم"] = new("greeting", null, "unknown", null, 0.9);
        _script["محمد أحمد علي حسن"] = new("provide_name", null, "unknown", "محمد أحمد علي حسن", 0.95);
        _script["عايز استشارة"] = new("consultation_request", null, "unknown", null, 0.9);
        _script["أخويا عنده قضية قتل"] = new("describe_matter", null, "unknown", null, 0.9, "criminal");
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        await Say("عايز استشارة");
        await Press(BookingChoice.Mode(BookingService.PhoneVideo));
        var d = await Together(Typed("أخويا عنده قضية قتل"), Pressed(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(19, 14))));
        Assert.Equal("criminal", Assert.Single(_booking.Appointments).MatterType);
        Assert.Equal(TextKeys.BookingHold, Keys(d)[0]); // the press is still answered by code
    }

    private Task<TurnDecision> Press(string id) =>
        Send(new IncomingMessage($"wamid.{++_turn}", MessageType.Interactive, null, null, null, Interactive: new Interactive("list_reply", id, "x")));

    /// <summary>A receipt: an image n8n stored (its place and hash only).</summary>
    private Task<TurnDecision> Receipt(string sha = "aa", string? caption = null) =>
        Send(new IncomingMessage($"wamid.{++_turn}", MessageType.Image, null, null, null, caption,
            new MediaRef(MediaStatus.Stored, "office-sensitive", $"k{_turn}", "image/jpeg", 1000, sha)));

    private static List<string> Keys(TurnDecision d) => d.Effects!.Replies.Select(r => r.Key).ToList();

    private static List<string> TaskKinds(TurnDecision d) => d.Effects!.Tasks.Select(t => t.Kind).ToList();

    /// <summary>A new client up to the offer: the name, then «عايز أحجز».</summary>
    private async Task<TurnDecision> ToTheOffer()
    {
        _script["السلام عليكم"] = new("greeting", null, "unknown", null, 0.9);
        _script["محمد أحمد علي حسن"] = new("provide_name", null, "unknown", "محمد أحمد علي حسن", 0.95);
        _script["عايز أحجز"] = new("consultation_request", null, "unknown", null, 0.9, "inheritance");
        await Say("السلام عليكم");
        await Say("محمد أحمد علي حسن");
        return await Say("عايز أحجز");
    }

    /// <summary>Held, then confirmed by a receipt (v1.13).</summary>
    private async Task<Guid> BookAndPay(BookingService service, DateTimeOffset at)
    {
        await Press(BookingChoice.Pick(service, at));
        await Receipt(sha: at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return _booking.Appointments.Single(a => a.State == AppointmentInfo.Confirmed && a.Start == at).Id;
    }

    [Fact]
    public async Task The_offer_asks_the_kind_with_two_buttons_without_the_ai_afterwards()
    {
        var d = await ToTheOffer();
        var offer = d.Actions.Single(a => a.Type == ActionType.SendInteractive);
        Assert.Equal(HazemFlowTests.Approved[TextKeys.ConsultationOffer], offer.Text);
        Assert.Equal([BookingChoice.Mode(BookingService.PhoneVideo), BookingChoice.Mode(BookingService.Office)], offer.Interactive!.Choices.Select(c => c.Id));

        var times = await Press(BookingChoice.Mode(BookingService.PhoneVideo));
        Assert.Null(times.Effects!.Classification); // a button is read without the AI
        var list = Assert.Single(times.Actions).Interactive!;
        Assert.Equal(InteractivePayload.List, list.Kind);
        Assert.Equal(["النهارده 2:00 م", "النهارده 2:15 م", "النهارده 2:30 م", "النهارده 2:45 م", "T:btn_more_slots"], list.Choices.Select(c => c.Title));
        Assert.Equal(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(19, 14)), list.Choices[0].Id);
    }

    [Fact]
    public async Task A_picked_time_is_held_with_where_to_pay_and_the_exact_deadline()
    {
        await ToTheOffer();
        var d = await Press(BookingChoice.Pick(BookingService.Office, AvailabilityTests.At(19, 15)));
        Assert.Equal([TextKeys.BookingHold], Keys(d));
        var text = Assert.Single(d.Actions).Text!;
        Assert.Contains("مقابلة في المكتب يوم النهارده الساعة 3:00 م", text);
        Assert.Contains("1,000 جنيه", text);
        Assert.Contains("office@instapay", text);
        Assert.Contains("01000000000 (فودافون كاش)", text);
        Assert.Contains("قبل الساعة 1:00 م النهارده", text); // within 6 hours: the short hold (1 hour), as a time of day
        var held = Assert.Single(_booking.Appointments);
        Assert.Equal((AppointmentInfo.PendingPayment, AvailabilityTests.At(19, 13), "inheritance"), (held.State, held.HoldUntil, held.MatterType));
    }

    [Fact]
    public void The_hold_is_two_hours_one_when_soon_and_always_ends_before_the_appointment()
    {
        var h = HoldRules.Default;
        Assert.Equal(Now.AddHours(2), h.Until(Now, Now.AddDays(1)));
        Assert.Equal(Now.AddHours(1), h.Until(Now, Now.AddHours(5)));
        Assert.Equal(Now.AddMinutes(30), h.Until(Now, Now.AddMinutes(45)));
        Assert.False(h.Offerable(Now, Now.AddMinutes(25))); // a quarter of an hour to pay is too little
        Assert.Equal("hold_short_minutes", (h with { ShortMinutes = 200 }).Problem());
    }

    [Fact]
    public async Task The_receipt_image_confirms_the_hold_without_the_ai()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        var d = await Receipt();
        Assert.Null(d.Effects!.Classification); // T-09: never to the AI
        Assert.Equal([TextKeys.BookingPaid], Keys(d));
        Assert.Contains("بكرة الساعة 12:00 م", Assert.Single(d.Actions).Text);
        var a = Assert.Single(_booking.Appointments);
        Assert.Equal(AppointmentInfo.Confirmed, a.State);
        Assert.Equal((PaymentInfo.ProofReceived, (DateTimeOffset?)_now), (_booking.Payments[a.Id].State, _booking.Payments[a.Id].ProofAt));
    }

    [Fact]
    public async Task Words_with_the_receipt_go_to_the_office_not_the_ai()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        var d = await Receipt(caption: "حولت من رقم أخويا");
        Assert.Null(d.Effects!.Classification);
        Assert.Equal([TextKeys.BookingPaid], Keys(d));
        Assert.Contains(PendingTask.UnclearMessage, TaskKinds(d));
    }

    [Fact]
    public async Task A_pdf_is_not_a_receipt_hazem_asks_for_an_image()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        var d = await Send(new IncomingMessage($"wamid.{++_turn}", MessageType.Document, null, null, null, null,
            new MediaRef(MediaStatus.Stored, "office-sensitive", "doc", "application/pdf", 1000, "bb", "receipt.pdf")));
        Assert.Equal([TextKeys.ReceiptSendImage], Keys(d));
        Assert.Equal(AppointmentInfo.PendingPayment, Assert.Single(_booking.Appointments).State);
    }

    [Fact]
    public async Task An_image_with_nothing_waiting_is_just_a_file()
    {
        await ToTheOffer();
        var d = await Receipt();
        Assert.Equal([TextKeys.MediaReceived], Keys(d));
        Assert.Empty(_booking.Appointments);
    }

    [Fact]
    public async Task One_hold_at_a_time_the_second_waits_for_the_first_receipt()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        var d = await Press(BookingChoice.Pick(BookingService.Office, AvailabilityTests.At(21, 12)));
        Assert.Equal([TextKeys.BookingHoldOne], Keys(d));
        Assert.Contains("لحد 2:00 م النهارده", Assert.Single(d.Actions).Text);
        Assert.Single(_booking.Appointments);
    }

    [Fact]
    public async Task Another_time_for_the_same_service_replaces_the_hold()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        var d = await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(21, 12)));
        Assert.Equal([TextKeys.BookingHold], Keys(d));
        Assert.Equal([AppointmentInfo.CancelledByClient, AppointmentInfo.PendingPayment], _booking.Appointments.Select(a => a.State));
    }

    [Fact]
    public async Task A_late_receipt_confirms_a_time_still_free_or_pays_for_the_next_one()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        _booking.Expire();
        Assert.Equal([TextKeys.BookingPaid], Keys(await Receipt(sha: "s1"))); // still free: confirmed
        Assert.Equal(AppointmentInfo.Confirmed, Assert.Single(_booking.Appointments).State);

        _booking.Appointments.Clear();
        _rows.Clear();
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        _booking.Expire();
        _calendar.Busy.Add(new BusyInterval(AvailabilityTests.At(20, 12), AvailabilityTests.At(20, 13))); // taken meanwhile
        var late = await Receipt(sha: "s2");
        Assert.Equal([TextKeys.BookingHoldLate], Keys(late));
        Assert.Contains("بكرة 12:00 م", late.Actions[0].Text);

        var picked = await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 14)));
        Assert.Equal([TextKeys.BookingPaid], Keys(picked)); // no second payment
        Assert.Equal(PaymentInfo.ProofReceived, _booking.Payments[_booking.Appointments.Single(a => a.State == AppointmentInfo.Confirmed).Id].State);
    }

    [Fact]
    public async Task Without_the_payment_details_the_hold_stands_and_the_office_gets_a_task()
    {
        _settings.Remove(OfficeInfo.InstaPayKey);
        await ToTheOffer();
        var d = await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(19, 14)));
        Assert.Empty(d.Actions); // never «InstaPay: » with nothing after it
        Assert.Contains(PendingTask.BookingMessageFailed, TaskKinds(d));
        Assert.Single(_booking.Appointments);
    }

    [Fact]
    public async Task A_time_taken_meanwhile_offers_other_times_and_the_calendar_counts()
    {
        await ToTheOffer();
        _calendar.Busy.Add(new BusyInterval(AvailabilityTests.At(19, 14), AvailabilityTests.At(19, 15)));
        var d = await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(19, 14)));
        Assert.Equal([TextKeys.BookingSlotTaken], Keys(d));
        Assert.Equal("النهارده 3:15 م", Assert.Single(d.Actions).Interactive!.Choices[0].Title);
        Assert.Empty(_booking.Appointments);
    }

    [Fact]
    public async Task No_calendar_no_times_and_a_task_for_the_office()
    {
        _calendar.Down = true;
        await ToTheOffer();
        var d = await Press(BookingChoice.Mode(BookingService.PhoneVideo));
        Assert.Equal([TextKeys.BookingUnavailable], Keys(d));
        Assert.Equal([PendingTask.BookingUnavailable], TaskKinds(d));
    }

    [Fact]
    public async Task A_closed_day_from_the_panel_is_never_offered()
    {
        _booking.Schedule = new Schedule(AvailabilityTests.Every(600, 1440), [new DayException(new DateOnly(2026, 11, 19), ExceptionKind.ClosedDay, null)]);
        await ToTheOffer();
        var list = Assert.Single((await Press(BookingChoice.Mode(BookingService.PhoneVideo))).Actions).Interactive!;
        Assert.Equal("بكرة 10:00 ص", list.Choices[0].Title);
    }

    [Fact]
    public async Task Urgent_shows_today_s_times_without_the_notice_but_with_time_to_pay()
    {
        await ToTheOffer();
        _now = AvailabilityTests.At(19, 12, 3);
        _script["مستعجل جدا"] = new("urgent", null, "unknown", null, 0.95);
        var d = await Say("مستعجل جدا");
        Assert.Equal([TextKeys.UrgentPrice, TextKeys.BookingChooseSlot], Keys(d));
        // 12:15 and 12:30 would leave under a quarter of an hour to pay (1.13): 12:45 is the first.
        Assert.Equal("النهارده 12:45 م", d.Actions[1].Interactive!.Choices[0].Title);
        Assert.Contains(PendingTask.UrgentRequest, TaskKinds(d));
    }

    [Fact]
    public async Task Change_once_then_the_lawyer_decides_and_cancel_within_24_hours_is_a_full_refund()
    {
        await ToTheOffer();
        var id = await BookAndPay(BookingService.PhoneVideo, AvailabilityTests.At(20, 12));

        _script["عايز ألغي أو أغير"] = new("reschedule_or_cancel", null, "unknown", null, 0.9);
        var shown = await Say("عايز ألغي أو أغير");
        Assert.Equal([BookingChoice.Change(id), BookingChoice.Cancel(id)], Assert.Single(shown.Actions).Interactive!.Choices.Select(c => c.Id));
        Assert.Contains("بكرة 12:00 م", shown.Actions[0].Text);

        var times = await Press(BookingChoice.Change(id));
        Assert.StartsWith($"bk:t:{id:N}:", Assert.Single(times.Actions).Interactive!.Choices[0].Id);
        var moved = await Press(BookingChoice.MoveTo(id, AvailabilityTests.At(20, 13)));
        Assert.Equal([TextKeys.BookingRescheduled], Keys(moved));
        Assert.Equal((AvailabilityTests.At(20, 13), 1), (_booking.Appointments[0].Start, _booking.Appointments[0].Reschedules));

        var again = await Press(BookingChoice.Change(id)); // P6: the second change goes to the lawyer
        Assert.Equal([TextKeys.RescheduleOrCancel], Keys(again));
        Assert.Equal([PendingTask.LateReschedule], TaskKinds(again));

        var cancelled = await Press(BookingChoice.Cancel(id));
        Assert.Equal([TextKeys.RefundFull], Keys(cancelled)); // B-22: within 24 hours of the receipt
        Assert.Equal([PendingTask.Refund], TaskKinds(cancelled));
        Assert.Equal(AppointmentInfo.CancelledByClient, _booking.Appointments[0].State);
        Assert.Equal((PaymentInfo.RefundRequested, (int?)500), (_booking.Payments[id].State, _booking.Refunds[id]));
    }

    [Fact]
    public async Task Cancel_after_24_hours_of_the_receipt_is_no_refund_and_a_hold_is_nothing_to_refund()
    {
        await ToTheOffer();
        var id = await BookAndPay(BookingService.PhoneVideo, AvailabilityTests.At(23, 12));
        _now = Now.AddHours(25);
        var d = await Press(BookingChoice.Cancel(id));
        Assert.Equal([TextKeys.RefundNone], Keys(d));
        Assert.Equal([PendingTask.BookingCancelled], TaskKinds(d));

        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(24, 12)));
        var hold = _booking.Appointments.Single(a => a.State == AppointmentInfo.PendingPayment).Id;
        var cancelledHold = await Press(BookingChoice.Cancel(hold));
        Assert.Equal([TextKeys.BookingHoldCancelled], Keys(cancelledHold));
        Assert.Empty(TaskKinds(cancelledHold));
    }

    [Fact]
    public async Task A_refund_press_with_nothing_to_give_back_is_never_promised()
    {
        await ToTheOffer();
        // The office cancelled it, but its money already pays another booking (or never came): nothing to refund.
        var cancelled = new AppointmentInfo(Guid.NewGuid(), _rows[Number].Person.Id, BookingService.PhoneVideo.Code, AvailabilityTests.At(21, 12),
            AvailabilityTests.At(21, 12, 15), AppointmentInfo.CancelledByOffice, 0, 500, null);
        _booking.Appointments.Add(cancelled);
        var d = await Press(BookingChoice.Refund(cancelled.Id));
        Assert.DoesNotContain(TextKeys.BookingRefundRequested, Keys(d));
        Assert.DoesNotContain(PendingTask.Refund, TaskKinds(d));
    }

    [Fact]
    public async Task A_hold_is_changed_by_picking_another_time_not_counted_as_the_free_change()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(19, 20)));
        var id = _booking.Appointments[0].Id;
        var d = await Press(BookingChoice.Change(id));
        Assert.StartsWith("bk:s:pv:", Assert.Single(d.Actions).Interactive!.Choices[0].Id); // a new pick, not a move
        Assert.Empty(TaskKinds(d));
    }

    [Fact]
    public async Task Less_than_12_hours_before_the_lawyer_decides()
    {
        await ToTheOffer();
        var id = await BookAndPay(BookingService.PhoneVideo, AvailabilityTests.At(19, 20));
        var d = await Press(BookingChoice.Change(id));
        Assert.Equal([PendingTask.LateReschedule], TaskKinds(d));
        Assert.Equal(AvailabilityTests.At(19, 20), _booking.Appointments[0].Start);
    }

    [Fact]
    public async Task Somebody_else_s_appointment_in_a_button_is_never_touched()
    {
        await ToTheOffer();
        var other = new AppointmentInfo(Guid.NewGuid(), Guid.NewGuid(), BookingService.PhoneVideo.Code, AvailabilityTests.At(21, 12),
            AvailabilityTests.At(21, 12, 15), AppointmentInfo.Confirmed, 0, 500, null);
        _booking.Appointments.Add(other);
        await Press(BookingChoice.Cancel(other.Id));
        Assert.Equal(AppointmentInfo.Confirmed, _booking.Appointments.Single(a => a.Id == other.Id).State);
    }

    [Fact]
    public async Task Without_the_approved_texts_or_the_switch_hazem_answers_as_before()
    {
        _texts.Remove(TextKeys.BookingHold); // one TASK 09 text missing is enough
        var d = await ToTheOffer();
        Assert.DoesNotContain(d.Actions, a => a.Type == ActionType.SendInteractive);

        _texts[TextKeys.BookingHold] = new ApprovedText(TextKeys.BookingHold, 1, "x", 600, true);
        _settings[BookingSettings.EnabledKey] = "false";
        _rows.Clear();
        d = await ToTheOffer();
        Assert.DoesNotContain(d.Actions, a => a.Type == ActionType.SendInteractive);
        Assert.Empty(_booking.Appointments);
    }

    [Fact]
    public async Task A_second_booking_of_the_same_service_shows_the_first_once_confirmed()
    {
        await ToTheOffer();
        await BookAndPay(BookingService.PhoneVideo, AvailabilityTests.At(20, 12));
        var d = await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(21, 12)));
        Assert.Equal([TextKeys.BookingExisting], Keys(d));
        Assert.Single(_booking.Appointments);
    }

    [Fact]
    public async Task Words_typed_with_a_press_are_not_lost_the_office_gets_a_task()
    {
        await ToTheOffer();
        var d = await Send(new Turn($"wt:{++_turn}", Number, null,
        [
            new IncomingMessage($"wamid.{++_turn}", MessageType.Interactive, null, null, null,
                Interactive: new Interactive("button_reply", BookingChoice.Mode(BookingService.PhoneVideo), "x")),
            new IncomingMessage($"wamid.{++_turn}", MessageType.Text, "وممكن أجيب معايا أخويا؟", null, null),
        ]));
        Assert.Equal(InteractivePayload.List, Assert.Single(d.Actions).Interactive!.Kind); // the press is answered
        Assert.Contains(PendingTask.UnclearMessage, TaskKinds(d)); // and the words go to the office

        var alone = await Press(BookingChoice.Mode(BookingService.PhoneVideo));
        Assert.DoesNotContain(PendingTask.UnclearMessage, TaskKinds(alone));
    }

    // ---------- TASK 10: the written services ----------

    private void ServicesOn()
    {
        foreach (var k in HazemResponder.ServiceTextKeys)
        {
            _texts[k] = new ApprovedText(k, 1, "T:" + k, 600, true);
        }

        _texts[TextKeys.MediaReceived] = new ApprovedText(TextKeys.MediaReceived, 1, "وصلت {{media}}", 300, true);
        _script["عايز حد يراجعلي عقد"] = new("service_by_name", "document_review", "unknown", null, 0.9);
    }

    private Task<TurnDecision> Files(params (MessageType Type, string Mime)[] files) =>
        Send(new Turn($"wt:{++_turn}", Number, null, files.Select(f => new IncomingMessage($"wamid.{++_turn}", f.Type, null, null, null, null,
            new MediaRef(MediaStatus.Stored, "office-sensitive", $"k{_turn}", f.Mime, 1000, $"sha{_turn}", "contract"))).ToList()));

    [Fact]
    public async Task A_named_service_opens_an_order_and_its_documents_never_reach_the_ai()
    {
        ServicesOn();
        await ToTheOffer();
        var asked = await Say("عايز حد يراجعلي عقد");
        Assert.Equal([TextKeys.DocumentReviewRequest], Keys(asked));
        var order = Assert.Single(_booking.Orders);
        Assert.Equal(("document_review", "collecting"), (order.Kind, order.State));

        var pages = await Files((MessageType.Image, "image/jpeg"), (MessageType.Image, "image/jpeg"));
        Assert.Null(pages.Effects!.Classification); // T-09
        Assert.Equal([TextKeys.ServiceFilesReceived], Keys(pages));
        Assert.Contains(PendingTask.ServicePricing, TaskKinds(pages));
        Assert.Equal(("awaiting_price", 2), (order.State, order.Files.Count));

        var more = await Files((MessageType.Document, "application/pdf")); // a PDF is a document here, not a receipt
        Assert.Equal([TextKeys.MediaReceived], Keys(more));
        Assert.DoesNotContain(PendingTask.ServicePricing, TaskKinds(more));
        Assert.Equal(3, order.Files.Count);
    }

    [Fact]
    public async Task With_a_hold_waiting_an_image_is_its_receipt_and_a_pdf_goes_to_the_order()
    {
        ServicesOn();
        await ToTheOffer();
        await Say("عايز حد يراجعلي عقد");
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        Assert.Equal([TextKeys.BookingPaid], Keys(await Files((MessageType.Image, "image/jpeg"))));
        Assert.Equal(AppointmentInfo.Confirmed, Assert.Single(_booking.Appointments).State);
        Assert.Equal([TextKeys.ServiceFilesReceived], Keys(await Files((MessageType.Document, "application/pdf"))));
    }

    [Fact]
    public async Task The_request_with_the_contract_in_one_turn_opens_the_order_with_it()
    {
        ServicesOn();
        await ToTheOffer();
        var both = await Send(new Turn($"wt:{++_turn}", Number, null,
        [
            new IncomingMessage($"wamid.{++_turn}", MessageType.Text, "عايز حد يراجعلي عقد", null, null),
            new IncomingMessage($"wamid.{++_turn}", MessageType.Document, null, null, null, null,
                new MediaRef(MediaStatus.Stored, "office-sensitive", $"k{_turn}", "application/pdf", 1000, $"sha{_turn}", "contract.pdf")),
        ]));
        Assert.Equal([TextKeys.ServiceFilesReceived], Keys(both)); // not «ابعتلي صورة العقد»
        Assert.Contains(PendingTask.ServicePricing, TaskKinds(both));
        var order = Assert.Single(_booking.Orders);
        Assert.Equal(("awaiting_price", 1), (order.State, order.Files.Count));
    }

    [Fact]
    public async Task A_message_from_the_client_sends_again_what_could_not_reach_him()
    {
        ServicesOn();
        await ToTheOffer();
        var before = _booking.Retries;
        await Say("شكرا");
        Assert.Equal(before + 1, _booking.Retries);
    }

    [Fact]
    public async Task Without_the_service_texts_a_named_service_is_answered_as_before()
    {
        await ToTheOffer();
        _script["عايز حد يراجعلي عقد"] = new("service_by_name", "document_review", "unknown", null, 0.9);
        var d = await Say("عايز حد يراجعلي عقد");
        Assert.Equal([TextKeys.DocumentReviewRequest], Keys(d));
        Assert.Contains(PendingTask.ServiceRequest, TaskKinds(d));
        Assert.Empty(_booking.Orders);
    }

    [Fact]
    public void Chatwoot_shows_the_choices_hazem_offered()
    {
        var a = new OutboundAction(ActionType.SendInteractive, "k", "اختار ميعاد",
            Interactive: new InteractivePayload(InteractivePayload.List, "المواعيد", [new("bk:s:pv:1", "بكره 2:00 م"), new("bk:n:pv:1", "مواعيد تانية")]));
        Assert.Equal("اختار ميعاد\n▫️ بكره 2:00 م\n▫️ مواعيد تانية", Office.Application.Chatwoot.ChatwootText.Hazem(a));
        Assert.Equal("اختار ميعاد", Office.Application.Chatwoot.ChatwootText.Hazem(a with { Interactive = null }));
    }

    // ---------- TASK 11: the Urfi paths (B-11, every row of its table) ----------

    private void UrfOn()
    {
        foreach (var k in HazemResponder.UrfTextKeys)
        {
            var max = k.StartsWith("btn_", StringComparison.Ordinal) ? 20 : 600;
            _texts[k] = new ApprovedText(k, 1, k == TextKeys.UrfChooseSlot ? "{{service}} {{amount}} {{documents}}"
                : k == TextKeys.UrfSendDocuments ? "ابعت {{documents}}" : "T:" + k[..Math.Min(k.Length, 14)], max, true);
        }

        _texts[TextKeys.MediaReceived] = new ApprovedText(TextKeys.MediaReceived, 1, "وصلت {{media}}", 300, true);
        _script["عايز أعمل جواز عرفي"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9);
        _script["عايز طلاق عرفي"] = new("service_by_name", "urf_divorce", "unknown", null, 0.9);
        _script["عرفي"] = new("service_by_name", "urf_unclear", "unknown", null, 0.9);
    }

    private Task<TurnDecision> Answer(string step, string value) => Press(Office.Application.Urf.UrfAnswer.Id(step, value));

    private Office.Application.Urf.UrfCase TheCase => Assert.Single(_booking.UrfCases);

    /// <summary>A new client asks for an Urfi marriage contract and answers up to the guardian.</summary>
    private async Task ToTheGuardian(string ages = "30 و 25", string past = "first")
    {
        UrfOn();
        await ToTheOffer();
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say("عايز أعمل جواز عرفي")));
        _script[ages] = new("other", null, "unknown", null, 0.9);
        Assert.Equal([TextKeys.UrfAskWifePast], Keys(await Say(ages)));
        Assert.Equal([TextKeys.UrfAskGuardian], Keys(await Answer(Office.Application.Urf.UrfStep.WifePast, past)));
    }

    [Theory]
    [InlineData("الاتنين 20", 20, 20)]
    [InlineData("الإتنين ٢٥", 25, 25)]
    [InlineData("كل واحد 30", 30, 30)]
    [InlineData("الزوج 20 و الزوجة 35", 20, 35)]
    public void Urf_one_age_said_for_both_is_both_ages(string text, int husband, int wife) =>
        Assert.Equal((husband, wife), Office.Application.Urf.UrfAges.Parse(text));

    [Fact]
    public void Urf_one_age_without_a_word_for_both_is_still_not_read() =>
        Assert.Null(Office.Application.Urf.UrfAges.Parse("الزوج 20"));

    /// <summary>v1.22: the owner's test, a question while the Urfi times were on the screen opened a second offer.</summary>
    [Fact]
    public async Task Urf_a_question_while_its_times_wait_gets_the_later_text_and_no_second_offer()
    {
        await ToTheGuardian();
        await Answer(Office.Application.Urf.UrfStep.Guardian, "father");
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Answer(Office.Application.Urf.UrfStep.Witnesses, "office")));
        _texts[TextKeys.UrfQuestionLater] = new ApprovedText(TextKeys.UrfQuestionLater, 1, "T:later", 300, true);
        _script["هل الشهود هيتكتمو على الزواج"] = new("legal_question", null, "unknown", null, 0.9, "urfi", "urf_marriage");
        var d = await Say("هل الشهود هيتكتمو على الزواج");
        Assert.Equal([TextKeys.UrfQuestionLater], Keys(d));
        Assert.DoesNotContain(d.Actions, a => a.Type == ActionType.SendInteractive);
    }

    [Fact]
    public async Task Urf_17_is_refused_without_a_price_or_a_time()
    {
        UrfOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        var d = await Say("الزوج 30 والزوجة ١٧");
        Assert.Equal([TextKeys.UrfUnderAge], Keys(d));
        Assert.Null(d.Effects!.Classification); // read by code, never the AI
        Assert.Equal((Office.Application.Urf.UrfCase.RejectedAge, Office.Application.Urf.UrfRules.PolicyVersion), (TheCase.State, TheCase.PolicyVersion));
        Assert.Empty(_booking.Appointments);
    }

    /// <summary>v1.20 (B-11.1): the owner's test of 2026-10-27, refused for the wife's age, then «why».</summary>
    private async Task RefusedForTheAge()
    {
        UrfOn();
        _texts[TextKeys.UrfAgeClosed] = new ApprovedText(TextKeys.UrfAgeClosed, 1, "T:closed", 300, true);
        _texts[TextKeys.Thanks] = new ApprovedText(TextKeys.Thanks, 1, "T:thanks", 300, true);
        _script["ليه"] = new("other", null, "unknown", null, 0.9);
        _script["ليه المكتب مش بيعمل عقود زواج للبنات الصغيره"] = new("legal_question", null, "unknown", null, 0.9, "personal_status", "marriage");
        _script["طب عايز أحجز استشارة"] = new("consultation_request", null, "unknown", null, 0.9);
        _script["عايز استشارة في ميراث"] = new("consultation_request", null, "unknown", null, 0.9, "inheritance");
        _script["شكرا يابطل"] = new("thanks_or_closing", null, "unknown", null, 1.0);
        _script["ممكن تقولي موعد صلاه الظهر اليوم"] = new("other", null, "unknown", null, 0.9);
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        Assert.Equal([TextKeys.UrfUnderAge], Keys(await Say("الزوج 22 والزوجه 16")));
    }

    [Fact]
    public async Task Urf_after_the_age_refusal_the_same_marriage_gets_the_closing_text_twice_then_silence()
    {
        await RefusedForTheAge();
        Assert.Equal([TextKeys.UrfAgeClosed], Keys(await Say("ليه")));
        Assert.Equal([TextKeys.UrfAgeClosed], Keys(await Say("ليه المكتب مش بيعمل عقود زواج للبنات الصغيره"))); // no B-20 offer
        var third = await Say("طب عايز أحجز استشارة");
        Assert.Empty(Keys(third));
        Assert.Empty(third.Actions); // no buttons either
        Assert.Empty(TaskKinds(third));
        Assert.Empty(_booking.Appointments);
    }

    [Fact]
    public async Task Urf_after_the_age_refusal_a_new_request_for_the_same_marriage_is_not_asked_again()
    {
        await RefusedForTheAge();
        Assert.Equal([TextKeys.UrfAgeClosed], Keys(await Say("عايز أعمل جواز عرفي"))); // not the ages again
    }

    [Fact]
    public async Task Urf_after_the_age_refusal_another_matter_gets_the_offer()
    {
        await RefusedForTheAge();
        Assert.Equal([TextKeys.ConsultationOffer], Keys(await Say("عايز استشارة في ميراث")));
    }

    /// <summary>The owner's test: after a thanks, «موعد صلاة الظهر» got the closing text. A message with no topic counts
    /// only before the client moves on; one about the marriage still does.</summary>
    [Fact]
    public async Task Urf_after_the_age_refusal_a_message_with_no_topic_is_answered_as_usual_once_the_client_moved_on()
    {
        await RefusedForTheAge();
        Assert.Equal([TextKeys.Thanks], Keys(await Say("شكرا يابطل")));
        Assert.DoesNotContain(TextKeys.UrfAgeClosed, Keys(await Say("ممكن تقولي موعد صلاه الظهر اليوم")));
        Assert.Equal([TextKeys.UrfAgeClosed], Keys(await Say("ليه المكتب مش بيعمل عقود زواج للبنات الصغيره"))); // named: still closed
    }

    [Fact]
    public async Task Urf_the_age_refusal_closes_the_matter_for_24_hours()
    {
        await RefusedForTheAge();
        _now = _now.AddHours(25);
        Assert.DoesNotContain(TextKeys.UrfAgeClosed, Keys(await Say("ليه المكتب مش بيعمل عقود زواج للبنات الصغيره")));
    }

    [Fact]
    public async Task Urf_without_the_closing_text_approved_the_refusal_is_followed_as_before()
    {
        await RefusedForTheAge();
        _texts[TextKeys.UrfAgeClosed] = _texts[TextKeys.UrfAgeClosed] with { Active = false };
        Assert.DoesNotContain(TextKeys.UrfAgeClosed, Keys(await Say("ليه المكتب مش بيعمل عقود زواج للبنات الصغيره")));
    }

    [Theory]
    [InlineData("brother", "الأخ")] // the father passed away, the brother is there
    [InlineData("uncle", "العم")]   // no father, no brother, the uncle is there
    public async Task Urf_the_first_guardian_there_is_the_guardian(string guardian, string name)
    {
        await ToTheGuardian();
        Assert.Equal([TextKeys.UrfAskWitnesses], Keys(await Answer(Office.Application.Urf.UrfStep.Guardian, guardian)));
        var times = await Answer(Office.Application.Urf.UrfStep.Witnesses, "own");
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(times));
        Assert.Contains($"بطاقة الولي ({name})", times.Actions.Single().Text);
        Assert.Contains("4,000", times.Actions.Single().Text); // the client's witnesses
        Assert.Contains("بطاقتين الشاهدين", times.Actions.Single().Text);
        Assert.Equal(Office.Application.Urf.UrfCase.DocumentsPending, TheCase.State);
    }

    [Fact]
    public async Task Urf_no_guardian_is_the_office_s_policy()
    {
        await ToTheGuardian();
        Assert.Equal([TextKeys.UrfNoGuardian], Keys(await Answer(Office.Application.Urf.UrfStep.Guardian, "none")));
        Assert.Equal(Office.Application.Urf.UrfCase.RejectedGuardian, TheCase.State);
    }

    [Fact]
    public async Task Urf_a_khul_asks_for_its_ruling_and_the_office_s_witnesses_cost_5000_for_60_minutes()
    {
        await ToTheGuardian(past: "khul");
        await Answer(Office.Application.Urf.UrfStep.Guardian, "father");
        var times = await Answer(Office.Application.Urf.UrfStep.Witnesses, "office");
        Assert.Contains("حكم الخلع", times.Actions.Single().Text);
        Assert.Contains("5,000", times.Actions.Single().Text);
        Assert.DoesNotContain("الشاهدين", times.Actions.Single().Text);

        var row = times.Actions.Single().Interactive!.Choices[0].Id;
        await Press(row);
        var held = Assert.Single(_booking.Appointments);
        Assert.Equal((BookingService.UrfMarriageOffice.Code, 5000, 60), (held.Service, held.Amount, (int)(held.End - held.Start).TotalMinutes));
        Assert.Equal((Office.Application.Urf.UrfCase.Booked, held.Id), (TheCase.State, TheCase.AppointmentId!.Value));

        // The receipt confirms it, then Hazem asks for the documents; the IDs go to the case, never the AI.
        Assert.Equal([TextKeys.BookingPaid, TextKeys.UrfSendDocuments], Keys(await Receipt()));
        var ids = await Files((MessageType.Image, "image/jpeg"), (MessageType.Image, "image/jpeg"));
        Assert.Null(ids.Effects!.Classification);
        Assert.Equal([TextKeys.UrfDocumentsReceived], Keys(ids));
        Assert.Contains(PendingTask.UrfReview, TaskKinds(ids));
        Assert.Equal(2, _booking.UrfFiles[TheCase.Id]);
    }

    [Fact]
    public async Task Urf_unclear_asks_what_is_wanted_without_choices()
    {
        UrfOn();
        await ToTheOffer();
        var d = await Say("عرفي");
        Assert.Equal([TextKeys.UrfUnclear], Keys(d));
        Assert.Null(d.Actions.Single().Interactive);
        Assert.Empty(_booking.UrfCases);
    }

    [Theory]
    [InlineData("has_copy", "no")]
    [InlineData("agreed", "no")]
    [InlineData("children", "yes")]
    public async Task Urf_divorce_goes_straight_to_its_path_and_a_hard_case_gets_the_consultation_only(string stop, string value)
    {
        UrfOn();
        await ToTheOffer();
        Assert.Equal([TextKeys.UrfAskCopy], Keys(await Say("عايز طلاق عرفي")));
        string[] steps = [Office.Application.Urf.UrfStep.HasCopy, Office.Application.Urf.UrfStep.Agreed, Office.Application.Urf.UrfStep.Children];
        TurnDecision last = null!;
        foreach (var step in steps)
        {
            last = await Answer(step, step == stop ? value : step == Office.Application.Urf.UrfStep.Children ? "no" : "yes");
            if (step == stop)
            {
                break;
            }
        }

        Assert.Equal([TextKeys.ConsultationOffer], Keys(last)); // the divorce service is not sold
        Assert.Equal(Office.Application.Urf.UrfCase.Redirected, TheCase.State);
    }

    [Fact]
    public async Task Urf_ages_that_cannot_be_read_are_asked_again_then_go_to_the_office()
    {
        UrfOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        Assert.Equal([TextKeys.UrfAgesAgain], Keys(await Say("الزوج 30"))); // one number: not both ages
        var d = await Say("حوالي 30");
        Assert.Contains(PendingTask.UrfHelp, TaskKinds(d));
        Assert.Equal(Office.Application.Urf.UrfCase.Collecting, TheCase.State);
    }

    [Theory]
    [InlineData("30 و 25", 30, 25)]
    [InlineData("الزوج ٣٥ والزوجة ٢٢", 35, 22)]
    [InlineData("25", null, null)]
    [InlineData("30 و 25 و 40", null, null)]
    [InlineData("3000 و 25", null, null)]
    public void Urf_ages_are_two_numbers_husband_first(string text, int? husband, int? wife)
    {
        var r = Office.Application.Urf.UrfAges.Parse(text);
        Assert.Equal(husband, r?.Husband);
        Assert.Equal(wife, r?.Wife);
    }

    [Fact]
    public async Task Urf_the_case_of_the_kind_asked_picks_up_where_it_was_never_a_second_one()
    {
        UrfOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        await Say("30 و 25"); // the marriage case waits at the wife's past
        Assert.Equal([TextKeys.UrfAskCopy], Keys(await Say("عايز طلاق عرفي")));
        Assert.Equal([TextKeys.UrfAskWifePast], Keys(await Say("عايز أعمل جواز عرفي"))); // the same marriage case again
        Assert.Equal(2, _booking.UrfCases.Count);
    }

    [Fact]
    public async Task Urf_words_without_a_number_at_the_ages_go_to_the_ai_and_a_human_request_is_heard()
    {
        UrfOn();
        await ToTheOffer();
        await Say("عايز أعمل جواز عرفي");
        _script["عايز أكلم موظف"] = new("human_request", null, "unknown", null, 0.9);
        var human = await Say("عايز أكلم موظف");
        Assert.NotNull(human.Effects!.Classification);
        Assert.Contains(PendingTask.HumanRequested, TaskKinds(human));

        _script["مش فاهم"] = new("other", null, "unknown", null, 0.9);
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say("مش فاهم"))); // unclear: the question still waiting, again
        Assert.Equal(0, TheCase.AgeTries);
    }

    [Fact]
    public async Task Urf_an_old_button_after_the_case_closed_never_reaches_the_ai()
    {
        await ToTheGuardian();
        await Answer(Office.Application.Urf.UrfStep.Guardian, "none"); // closed: no guardian
        var old = await Answer(Office.Application.Urf.UrfStep.WifePast, "first");
        Assert.Null(old.Effects!.Classification);
        Assert.Equal([TextKeys.UrfUnclear], Keys(old));
    }

    [Fact]
    public async Task Without_the_urf_texts_a_urf_request_is_answered_as_before()
    {
        await ToTheOffer();
        _script["عايز أعمل جواز عرفي"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9);
        var d = await Say("عايز أعمل جواز عرفي");
        Assert.DoesNotContain(TextKeys.UrfAskAges, Keys(d));
        Assert.Contains(PendingTask.ServiceRequest, TaskKinds(d)); // the office takes it, as before TASK 11
        Assert.Empty(_booking.UrfCases);
    }

    // ---------- TASK 06: the price list and the office's questions (B-20, B-23) ----------

    private void OfficeOn()
    {
        foreach (var k in new[]
                 {
                     TextKeys.OfficeAddress, TextKeys.OfficeHours, TextKeys.OfficeHowToBook, TextKeys.OfficeHowToPay, TextKeys.OfficeRefund,
                     TextKeys.OfficePrices, TextKeys.OfficeFeePolicy, TextKeys.OfficePracticeAreas,
                 })
        {
            _texts[k] = new ApprovedText(k, 1, "T:" + k[..Math.Min(k.Length, 14)], 600, true);
        }

        _texts[TextKeys.OfficeAddress] = new ApprovedText(TextKeys.OfficeAddress, 1, "📍 {{address}} {{map}}", 300, true);
        _texts[TextKeys.OfficeHours] = new ApprovedText(TextKeys.OfficeHours, 1, "🕐 {{hours}}.", 300, true);
        _script["المكتب فين"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "address");
        _script["بتشتغلوا لحد إمتى"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "hours");
        _script["أونلاين ولا في المكتب"] = new("office_info", null, "unknown", null, 0.9, InfoTopic: "mode");
        _script["المكتب فين بقى"] = new("office_info", null, "unknown", null, 0.9); // no topic: not an office question
    }

    [Fact]
    public async Task Office_the_address_from_the_panel_then_the_offer_and_never_half_a_message_without_it()
    {
        OfficeOn();
        await ToTheOffer();
        var none = await Say("المكتب فين");
        Assert.Equal([TextKeys.ConsultationOffer], Keys(none)); // no address written yet: the offer alone
        Assert.Contains(PendingTask.OfficeInfoMissing, TaskKinds(none));

        _settings[OfficeInfo.AddressKey] = "183 شارع التحرير";
        _settings[OfficeInfo.MapUrlKey] = "https://maps.example.test/office";
        var d = await Say("المكتب فين");
        Assert.Equal([TextKeys.OfficeAddress, TextKeys.ConsultationOffer], Keys(d));
        Assert.Equal("📍 183 شارع التحرير https://maps.example.test/office", d.Actions[0].Text);
        Assert.DoesNotContain(PendingTask.OfficeInfoMissing, TaskKinds(d));
    }

    [Fact]
    public async Task Office_the_hours_come_from_the_panel_s_default_week()
    {
        OfficeOn();
        _booking.Schedule = new(AvailabilityTests.Every(600, 1080, DayOfWeek.Friday), []);
        await ToTheOffer();
        var d = await Say("بتشتغلوا لحد إمتى");
        Assert.Equal("🕐 السبت لـ الخميس من 10:00 ص لـ 6:00 م، والجمعة إجازة.", d.Actions[0].Text);
    }

    [Fact]
    public async Task Office_phone_or_office_is_the_offer_itself_and_a_topicless_office_question_is_unclear()
    {
        OfficeOn();
        await ToTheOffer();
        Assert.Equal([TextKeys.ConsultationOffer], Keys(await Say("أونلاين ولا في المكتب")));
        Assert.DoesNotContain(TextKeys.OfficeAddress, Keys(await Say("المكتب فين بقى")));
    }

    [Theory]
    [InlineData(new[] { DayOfWeek.Friday }, 600, 1080, "السبت لـ الخميس من 10:00 ص لـ 6:00 م، والجمعة إجازة")]
    [InlineData(new DayOfWeek[0], 600, 1440, "كل يوم من 10:00 ص لـ 12 بالليل")]
    [InlineData(new[] { DayOfWeek.Friday, DayOfWeek.Saturday }, 720, 1230, "السبت إجازة، والأحد لـ الخميس من 12 الضهر لـ 8:30 م، والجمعة إجازة")]
    public void Office_the_week_in_hazem_s_words(DayOfWeek[] off, int start, int end, string expected) =>
        Assert.Equal(expected, BookingWords.WeekHours(AvailabilityTests.Every(start, end, off)));

    [Fact]
    public void Office_a_week_with_no_open_day_says_nothing() =>
        Assert.Null(BookingWords.WeekHours(WeeklyHours.Create([]).Hours!));

    [Fact]
    public async Task Prices_a_new_price_is_asked_for_and_quoted_and_an_earlier_hold_keeps_its_own()
    {
        await ToTheOffer();
        var first = await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        Assert.Equal(500, Assert.Single(_booking.Appointments).Amount);

        _settings[Office.Application.Prices.PriceList.PhoneVideo] = "600";
        var prices = Office.Application.Prices.PriceList.From(new HazemSettings(_settings));
        Assert.Equal(600, BookingSettings.From(new HazemSettings(_settings)).Fit(BookingService.PhoneVideo).Amount);
        Assert.Equal("بالتليفون بـ600", prices.Fill("بالتليفون بـ{{price_phone}}"));
        Assert.Equal(500, Assert.Single(_booking.Appointments).Amount); // the hold made before keeps 500
        Assert.NotEmpty(first.Actions);
    }

    [Theory]
    [InlineData("0", 500)]
    [InlineData("abc", 500)]
    [InlineData("12.5", 500)]
    [InlineData("2000000", 500)]
    [InlineData("0750", 500)] // a leading zero: hazem.price in SQL falls back too, so both sides agree
    [InlineData("1000000", 1000000)]
    [InlineData("750", 750)]
    public void Prices_a_broken_setting_falls_back_to_b23(string value, int expected) =>
        Assert.Equal(expected, Office.Application.Prices.PriceList.From(
            new HazemSettings(new Dictionary<string, string> { [Office.Application.Prices.PriceList.PhoneVideo] = value }))[Office.Application.Prices.PriceList.PhoneVideo]);

    [Fact]
    public void Prices_the_whole_list_is_checked_before_it_is_saved()
    {
        var ok = Office.Application.Prices.PriceList.Lines.ToDictionary(l => l.Key, l => l.Default);
        Assert.Null(Office.Application.Prices.PriceList.Problem(ok));
        Assert.Equal("review_range", Office.Application.Prices.PriceList.Problem(
            new Dictionary<string, int>(ok) { [Office.Application.Prices.PriceList.ReviewMin] = 4000 }));
        Assert.Equal("invalid_price", Office.Application.Prices.PriceList.Problem(
            new Dictionary<string, int>(ok) { [Office.Application.Prices.PriceList.Urgent] = 0 }));
        Assert.Equal("invalid_price", Office.Application.Prices.PriceList.Problem(ok.Where(kv => kv.Key != ok.Keys.First()).ToDictionary()));
    }

    // ---------- fakes ----------

    /// <summary>hazem.urf_cases and add_urf_file, in memory (the cases live in <see cref="MemoryBooking"/> for file_target).</summary>
    // ---------- TASK 12 (B-18): the reminder's two buttons ----------

    /// <summary>The reminder texts approved (they run with the booking from WhatsApp on or off).</summary>
    private void RemindersOn()
    {
        foreach (var k in ReminderTexts.Keys)
        {
            _texts[k] = new ApprovedText(k, 1, k.StartsWith("btn_", StringComparison.Ordinal) ? "T:" + k[..Math.Min(k.Length, 16)] : "T:" + k, 300, true);
        }

        _texts[TextKeys.ReminderAttendOk] = new ApprovedText(TextKeys.ReminderAttendOk, 1, "تمام في انتظارك {{day}} الساعة {{time}}", 300, true);
        _texts[TextKeys.ReminderOldButton] = new ApprovedText(TextKeys.ReminderOldButton, 1, "اتغيّر، دلوقتي {{day}} الساعة {{time}}", 300, true);
    }

    [Fact]
    public async Task I_will_come_is_recorded_for_the_reminder_s_time_only()
    {
        RemindersOn();
        await ToTheOffer();
        var id = await BookAndPay(BookingService.PhoneVideo, AvailabilityTests.At(20, 12));

        var d = await Press(ReminderButtons.Attend(id, AvailabilityTests.At(20, 12)));
        Assert.Null(d.Effects!.Classification); // read without the AI
        Assert.Equal([TextKeys.ReminderAttendOk], Keys(d));
        Assert.Equal("تمام في انتظارك بكرة الساعة 12:00 م", Assert.Single(d.Actions).Text);
        Assert.Contains((id, AvailabilityTests.At(20, 12)), _booking.Attending);
        Assert.Empty(TaskKinds(d));

        // The appointment moved: the old reminder's button tells the new time and records nothing.
        await Press(BookingChoice.Change(id));
        await Press(BookingChoice.MoveTo(id, AvailabilityTests.At(20, 13)));
        var old = await Press(ReminderButtons.Attend(id, AvailabilityTests.At(20, 12)));
        Assert.Equal([TextKeys.ReminderOldButton], Keys(old));
        Assert.Equal("اتغيّر، دلوقتي بكرة الساعة 1:00 م", Assert.Single(old.Actions).Text);
        Assert.DoesNotContain((id, AvailabilityTests.At(20, 13)), _booking.Attending);
    }

    [Fact]
    public async Task Change_or_cancel_from_the_reminder_is_the_existing_step_with_its_rules()
    {
        RemindersOn();
        await ToTheOffer();
        var id = await BookAndPay(BookingService.PhoneVideo, AvailabilityTests.At(20, 12));

        var d = await Press(ReminderButtons.Change(id));
        Assert.Equal([TextKeys.BookingExisting], Keys(d));
        Assert.Equal([BookingChoice.Change(id), BookingChoice.Cancel(id)], Assert.Single(d.Actions).Interactive!.Choices.Select(c => c.Id));
        var cancelled = await Press(BookingChoice.Cancel(id)); // B-22 as it was: within 24 hours of the receipt
        Assert.Equal([TextKeys.RefundFull], Keys(cancelled));

        // A cancelled appointment's «هحضر»: never recorded, the client's current booking or the office.
        var late = await Press(ReminderButtons.Attend(id, AvailabilityTests.At(20, 12)));
        Assert.Equal([TextKeys.RescheduleOrCancel], Keys(late));
        Assert.Empty(_booking.Attending);
    }

    [Fact]
    public async Task With_the_booking_off_the_reminder_still_works_and_a_change_goes_to_the_office()
    {
        RemindersOn();
        await ToTheOffer();
        var id = await BookAndPay(BookingService.Office, AvailabilityTests.At(20, 12));
        _settings[BookingSettings.EnabledKey] = "false";

        Assert.Equal([TextKeys.ReminderAttendOk], Keys(await Press(ReminderButtons.Attend(id, AvailabilityTests.At(20, 12)))));
        var change = await Press(ReminderButtons.Change(id));
        Assert.Equal([TextKeys.RescheduleOrCancel], Keys(change));
        Assert.Equal([PendingTask.RescheduleOrCancel], TaskKinds(change));
    }

    [Fact]
    public async Task Another_client_s_appointment_id_in_a_button_is_never_trusted()
    {
        RemindersOn();
        await ToTheOffer();
        var other = new AppointmentInfo(Guid.NewGuid(), Guid.NewGuid(), BookingService.PhoneVideo.Code, AvailabilityTests.At(21, 12),
            AvailabilityTests.At(21, 12, 15), AppointmentInfo.Confirmed, 0, 500, null);
        _booking.Appointments.Add(other);
        var d = await Press(ReminderButtons.Attend(other.Id, other.Start));
        Assert.DoesNotContain(TextKeys.ReminderAttendOk, Keys(d));
        Assert.Empty(_booking.Attending);
    }

    private sealed class MemoryUrf(MemoryBooking booking, Func<DateTimeOffset> now) : Office.Application.Urf.IUrfStore
    {
        public Task<Office.Application.Urf.AgeRefusal?> AgeRefusalAsync(Guid personId, DateTimeOffset since, CancellationToken ct) =>
            Task.FromResult(booking.AgeRefusedAt is { } at && at > since
                ? new Office.Application.Urf.AgeRefusal(at, booking.AgeClosingReplies, booking.AgeMovedOn)
                : null);

        public Task<IReadOnlyList<Office.Application.Urf.UrfCase>> OpenAsync(Guid personId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Office.Application.Urf.UrfCase>>(booking.UrfCases.Where(c => c.PersonId == personId
                && c.State is Office.Application.Urf.UrfCase.Collecting or Office.Application.Urf.UrfCase.DocumentsPending or Office.Application.Urf.UrfCase.Booked)
                .Reverse().ToList());

        public Task SaveAsync(Office.Application.Urf.UrfCase c, Guid? intakeId, CancellationToken ct)
        {
            // In place (the store keeps a case's creation order); a new case is the newest.
            var i = booking.UrfCases.FindIndex(x => x.Id == c.Id);
            if (c.State == Office.Application.Urf.UrfCase.RejectedAge && (i < 0 || booking.UrfCases[i].State != c.State))
            {
                booking.AgeRefusedAt = now(); // updated_at when the case became rejected_age
                (booking.AgeClosingReplies, booking.AgeMovedOn) = (0, false);
            }

            if (i >= 0)
            {
                booking.UrfCases[i] = c;
            }
            else
            {
                booking.UrfCases.Add(c);
            }

            if (booking.UrfCases.Count(x => x.PersonId == c.PersonId && x.Kind == c.Kind
                    && x.State is Office.Application.Urf.UrfCase.Collecting or Office.Application.Urf.UrfCase.DocumentsPending or Office.Application.Urf.UrfCase.Booked) > 1)
            {
                throw new InvalidOperationException("urf_cases_one_open"); // as the database's unique index
            }

            return Task.CompletedTask;
        }

        public async Task<Office.Application.Services.AddFileOutcome> AddFileAsync(Guid personId, Office.Application.Services.StoredFile file,
            CancellationToken ct)
        {
            if (await booking.FileTargetAsync(personId, file.MimeType?.StartsWith("image/", StringComparison.Ordinal) == true, ct)
                is not { Target: Office.Application.Services.FileTarget.UrfDocuments, Id: { } id })
            {
                return new(Office.Application.Services.AddFileStatus.None, null, null);
            }

            var count = booking.UrfFiles.GetValueOrDefault(id);
            booking.UrfFiles[id] = count + 1;
            return new(count == 0 ? Office.Application.Services.AddFileStatus.First : Office.Application.Services.AddFileStatus.More, id, null);
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    private sealed class Store(Dictionary<string, ClientState> rows) : IConversationStore
    {
        public Task<ClientState?> LoadAsync(string waId, CancellationToken ct) =>
            Task.FromResult(rows.TryGetValue(waId, out var s) ? s with { ClosedIntakes = [] } : null);

        public Task SaveConversationAsync(Domain.Clients.Conversation conversation, CancellationToken ct) => Task.CompletedTask;
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

    private sealed class Scripted(Dictionary<string, RawClassification> script) : IIntentClassifier
    {
        public Task<ClassifierOutcome> ClassifyAsync(string text, HazemSettings settings, CancellationToken ct) =>
            Task.FromResult(new ClassifierOutcome(script.TryGetValue(text, out var r) ? r : new("other", null, "unknown", null, 0.9), null, "fake", "v", 1));
    }

    private sealed class FakeCalendar : IBusyCalendar
    {
        public bool Down { get; set; }

        public List<BusyInterval> Busy { get; } = [];

        public Task<CalendarBusy> BusyAsync(DateTimeOffset from, DateTimeOffset to, HazemSettings settings, CancellationToken ct) =>
            Task.FromResult(Down ? CalendarBusy.Unavailable("down") : new CalendarBusy(true, Busy.ToList()));
    }

    /// <summary>The same rules as the SQL functions of migrations 0013 and 0014, in memory.</summary>
    private sealed class MemoryBooking(Func<DateTimeOffset> now) : IBookingStore, Office.Application.Services.IServiceStore
    {
        private readonly Dictionary<string, Guid> _byTurn = [];
        private readonly HashSet<string> _receipts = [];

        public Schedule Schedule { get; set; } = new(AvailabilityTests.Every(600, 1440), []);

        public List<AppointmentInfo> Appointments { get; } = [];

        public Dictionary<Guid, PaymentInfo> Payments { get; } = [];

        public Dictionary<Guid, int> Refunds { get; } = [];

        /// <summary>The worker's hazem.booking_due: every hold runs out now.</summary>
        public void Expire() => Set(a => a.State == AppointmentInfo.PendingPayment, AppointmentInfo.Expired);

        public Task<Schedule> LoadScheduleAsync(DateOnly from, DateOnly to, CancellationToken ct) => Task.FromResult(Schedule);

        public Task<IReadOnlyList<AppointmentInfo>> TakenBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AppointmentInfo>>(Appointments.Where(a => a.IsOpen && a.Start < to && a.End > from).ToList());

        public Task<AppointmentInfo?> UpcomingForAsync(Guid personId, DateTimeOffset at, CancellationToken ct) =>
            Task.FromResult(Appointments.Where(a => a.PersonId == personId && a.IsOpen && a.End > at).OrderBy(a => a.Start).FirstOrDefault());

        public Task<AppointmentInfo?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Appointments.FirstOrDefault(a => a.Id == id));

        public Task<BookOutcome> BookAsync(BookRequest r, CancellationToken ct)
        {
            if (_byTurn.TryGetValue(r.TurnId, out var existing))
            {
                return Done(BookStatus.Exists, existing);
            }

            if (Appointments.FirstOrDefault(a => a.PersonId == r.PersonId && a.Service == r.Service.Code && a.State == AppointmentInfo.Confirmed) is { } same)
            {
                return Done(BookStatus.AlreadyBooked, same.Id);
            }

            var until = r.HoldUntil;
            if (Appointments.FirstOrDefault(a => a.PersonId == r.PersonId && a.State == AppointmentInfo.PendingPayment) is { } hold)
            {
                if (hold.Service != r.Service.Code && hold.HoldUntil > now())
                {
                    return Done(BookStatus.HasHold, hold.Id);
                }

                until = hold.HoldUntil > now() && hold.HoldUntil < until ? hold.HoldUntil.Value : until;
                Set(a => a.Id == hold.Id, AppointmentInfo.CancelledByClient); // given up, never an expired hold that could take a late receipt
            }

            var credit = Appointments.Where(a => a.PersonId == r.PersonId && a.Service == r.Service.Code
                                                  && a.State is AppointmentInfo.Expired or AppointmentInfo.CancelledByOffice
                                                  && Payments.TryGetValue(a.Id, out var p) && p.ProofAt is not null
                                                  && p.State is not (PaymentInfo.NotReceived or PaymentInfo.RefundRequested or PaymentInfo.Refunded))
                .Select(a => a.Id).FirstOrDefault();
            var paid = credit != Guid.Empty;
            Appointments.Add(new AppointmentInfo(r.Id, r.PersonId, r.Service.Code, r.Start, r.Start.AddMinutes(r.Service.Minutes),
                paid ? AppointmentInfo.Confirmed : AppointmentInfo.PendingPayment, 0, r.Service.Amount, r.MatterType, paid ? null : until));
            if (paid)
            {
                Payments[r.Id] = Payments[credit];
                Payments.Remove(credit);
            }
            else
            {
                Payments[r.Id] = new PaymentInfo(PaymentInfo.Requested, r.Service.Amount, null, null);
            }

            _byTurn[r.TurnId] = r.Id;
            return Done(paid ? BookStatus.BookedPaid : BookStatus.Held, r.Id);
        }

        public Task<MoveStatus> MoveAsync(Guid id, DateTimeOffset start, DateTimeOffset end, string turnId, int bufferMinutes, CancellationToken ct)
        {
            var i = Appointments.FindIndex(a => a.Id == id);
            if (i < 0 || Appointments[i].Reschedules >= 1 || Appointments[i].State != AppointmentInfo.Confirmed)
            {
                return Task.FromResult(i < 0 ? MoveStatus.NotFound : MoveStatus.NotAllowed);
            }

            Appointments[i] = Appointments[i] with { Start = start, End = end, Reschedules = Appointments[i].Reschedules + 1 };
            return Task.FromResult(MoveStatus.Moved);
        }

        public Task<bool> CancelAsync(Guid id, string state, CancellationToken ct) =>
            Task.FromResult(Set(a => a.Id == id && a.IsOpen, state) == 1);

        /// <summary>TASK 12: «هحضر ✅», by appointment and its time (as the SQL in BookingStore).</summary>
        public HashSet<(Guid, DateTimeOffset)> Attending { get; } = [];

        public Task<bool> ConfirmAttendanceAsync(Guid id, DateTimeOffset start, CancellationToken ct)
        {
            var ok = Appointments.Any(a => a.Id == id && a.State == AppointmentInfo.Confirmed && a.Start == start);
            if (ok)
            {
                Attending.Add((id, start));
            }

            return Task.FromResult(ok);
        }

        public Task<AppointmentInfo?> ReceiptTargetAsync(Guid personId, DateTimeOffset at, CancellationToken ct)
        {
            var mine = Appointments.Where(a => a.PersonId == personId).ToList();
            bool Waits(AppointmentInfo a, params string[] states) => Payments.TryGetValue(a.Id, out var p) && states.Contains(p.State);
            return Task.FromResult(
                mine.FirstOrDefault(a => a.State == AppointmentInfo.PendingPayment)
                ?? mine.Where(a => a.State == AppointmentInfo.Confirmed && a.End > at
                                   && Waits(a, PaymentInfo.Requested, PaymentInfo.Partial, PaymentInfo.NotReceived)).OrderBy(a => a.Start).FirstOrDefault()
                ?? mine.LastOrDefault(a => a.State == AppointmentInfo.Expired && a.Start > at && Waits(a, PaymentInfo.Requested)));
        }

        public async Task<ReceiptOutcome> AttachReceiptAsync(ReceiptRequest r, bool lateFree, int bufferMinutes, CancellationToken ct)
        {
            if (!_receipts.Add(r.Wamid))
            {
                return new ReceiptOutcome(ReceiptStatus.Exists, null);
            }

            if (await ReceiptTargetAsync(r.PersonId, now(), ct) is not { } a)
            {
                return new ReceiptOutcome(ReceiptStatus.None, null);
            }

            var status = a.State switch
            {
                AppointmentInfo.PendingPayment => ReceiptStatus.Confirmed,
                AppointmentInfo.Confirmed => ReceiptStatus.Received,
                _ => lateFree ? ReceiptStatus.ConfirmedLate : ReceiptStatus.LateTaken,
            };
            if (status is ReceiptStatus.Confirmed or ReceiptStatus.ConfirmedLate)
            {
                Set(x => x.Id == a.Id, AppointmentInfo.Confirmed);
            }

            var p = Payments[a.Id];
            Payments[a.Id] = p with { State = PaymentInfo.ProofReceived, ProofAt = p.ProofAt ?? now() };
            return new ReceiptOutcome(status, a.Id);
        }

        public Task<PaymentInfo?> PaymentAsync(Guid appointmentId, CancellationToken ct) =>
            Task.FromResult(Payments.TryGetValue(appointmentId, out var p) ? p : null);

        public Task<int?> RequestRefundAsync(Guid appointmentId, string reason, CancellationToken ct)
        {
            if (!Payments.TryGetValue(appointmentId, out var p)
                || p.State is not (PaymentInfo.ProofReceived or PaymentInfo.DuplicateSuspected or PaymentInfo.Verified or PaymentInfo.Partial))
            {
                return Task.FromResult<int?>(null);
            }

            Payments[appointmentId] = p with { State = PaymentInfo.RefundRequested };
            Refunds[appointmentId] = p.AmountReceived ?? p.AmountDue;
            return Task.FromResult<int?>(Refunds[appointmentId]);
        }

        private int Set(Func<AppointmentInfo, bool> which, string state)
        {
            var changed = 0;
            for (var i = 0; i < Appointments.Count; i++)
            {
                if (which(Appointments[i]))
                {
                    Appointments[i] = Appointments[i] with { State = state };
                    changed++;
                }
            }

            return changed;
        }

        private static Task<BookOutcome> Done(BookStatus status, Guid id) => Task.FromResult(new BookOutcome(status, id));

        // ---------- TASK 10: the service orders, as migration 0015's functions ----------

        public List<Order> Orders { get; } = [];

        public List<Office.Application.Urf.UrfCase> UrfCases { get; } = [];

        public DateTimeOffset? AgeRefusedAt { get; set; }

        public int AgeClosingReplies { get; set; }

        public bool AgeMovedOn { get; set; }

        public Dictionary<Guid, int> UrfFiles { get; } = [];

        public sealed class Order(Guid person, string kind)
        {
            public Guid Id { get; } = Guid.NewGuid();
            public Guid Person { get; } = person;
            public string Kind { get; } = kind;
            public string State { get; set; } = "collecting";
            public List<string> Files { get; } = [];
        }

        /// <summary>
        /// hazem.file_target: a hold or a booking waiting for a receipt first, then an Urfi case's documents, then an order's
        /// documents, then a late hold.
        /// </summary>
        public async Task<Office.Application.Services.FileFor> FileTargetAsync(Guid personId, bool image, CancellationToken ct)
        {
            var docs = Orders.LastOrDefault(o => o.Person == personId && o.State is "collecting" or "awaiting_price");
            var urf = UrfCases.LastOrDefault(c => c.PersonId == personId
                && c.State is Office.Application.Urf.UrfCase.Booked or Office.Application.Urf.UrfCase.Approved
                && Appointments.Any(a => a.Id == c.AppointmentId && a.State is AppointmentInfo.PendingPayment or AppointmentInfo.Confirmed));
            var receipt = await ReceiptTargetAsync(personId, now(), ct);
            var kind = receipt?.State switch
            {
                AppointmentInfo.PendingPayment => Office.Application.Services.FileTarget.Hold,
                AppointmentInfo.Confirmed => Office.Application.Services.FileTarget.BookingPayment,
                AppointmentInfo.Expired => Office.Application.Services.FileTarget.LateHold,
                _ => Office.Application.Services.FileTarget.None,
            };
            if (image)
            {
                return kind is Office.Application.Services.FileTarget.Hold or Office.Application.Services.FileTarget.BookingPayment
                    ? new(kind, receipt!.Id)
                    : urf is not null ? new(Office.Application.Services.FileTarget.UrfDocuments, urf.Id)
                    : docs is not null ? new(Office.Application.Services.FileTarget.OrderDocuments, docs.Id)
                    : receipt is not null ? new(kind, receipt.Id)
                    : Office.Application.Services.FileFor.Nothing;
            }

            return urf is not null ? new(Office.Application.Services.FileTarget.UrfDocuments, urf.Id)
                : docs is not null ? new(Office.Application.Services.FileTarget.OrderDocuments, docs.Id)
                : receipt is not null ? new(Office.Application.Services.FileTarget.AskImage, receipt.Id)
                : Office.Application.Services.FileFor.Nothing;
        }

        public Task<Office.Application.Services.OpenOrderOutcome> OpenAsync(Guid personId, Guid? intakeId, Office.Application.Services.ServiceKind kind,
            string key, CancellationToken ct)
        {
            if (Orders.FirstOrDefault(o => o.Person == personId && o.Kind == kind.Code && o.State is not ("delivered" or "closed")) is { } open)
            {
                return Task.FromResult(new Office.Application.Services.OpenOrderOutcome(Office.Application.Services.OpenOrderStatus.Open, open.Id, open.State));
            }

            var o = new Order(personId, kind.Code);
            Orders.Add(o);
            return Task.FromResult(new Office.Application.Services.OpenOrderOutcome(Office.Application.Services.OpenOrderStatus.Opened, o.Id, o.State));
        }

        public int Retries { get; private set; }

        public Task RetryNoticesAsync(Guid personId, CancellationToken ct)
        {
            Retries++;
            return Task.CompletedTask;
        }

        public async Task<Office.Application.Services.AddFileOutcome> AddFileAsync(Guid personId, Office.Application.Services.StoredFile file, CancellationToken ct)
        {
            if ((await FileTargetAsync(personId, false, ct)) is not { Target: Office.Application.Services.FileTarget.OrderDocuments } t)
            {
                return new(Office.Application.Services.AddFileStatus.None, null, null);
            }

            var o = Orders.Single(x => x.Id == t.Id);
            var first = o.State == "collecting";
            o.Files.Add(file.Key);
            o.State = "awaiting_price";
            return new(first ? Office.Application.Services.AddFileStatus.First : Office.Application.Services.AddFileStatus.More, o.Id, o.Kind);
        }
    }
}
