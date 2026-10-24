using Office.Application.Booking;
using Office.Application.Conversations;
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
        new BookingDesk(_booking, _calendar));

    private Task<TurnDecision> Send(IncomingMessage m) => Send(new Turn($"wt:{++_turn}", Number, null, [m]));

    private async Task<TurnDecision> Send(Turn turn)
    {
        var d = await Hazem.DecideAsync(turn, new HazemSettings(_settings), CancellationToken.None);
        if (d.Effects is { } e)
        {
            _rows[Number] = e.State;
        }

        return d;
    }

    private Task<TurnDecision> Say(string text) => Send(new IncomingMessage($"wamid.{++_turn}", MessageType.Text, text, null, null));

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
        Assert.Equal([AppointmentInfo.Expired, AppointmentInfo.PendingPayment], _booking.Appointments.Select(a => a.State));
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

    [Fact]
    public void Chatwoot_shows_the_choices_hazem_offered()
    {
        var a = new OutboundAction(ActionType.SendInteractive, "k", "اختار ميعاد",
            Interactive: new InteractivePayload(InteractivePayload.List, "المواعيد", [new("bk:s:pv:1", "بكره 2:00 م"), new("bk:n:pv:1", "مواعيد تانية")]));
        Assert.Equal("اختار ميعاد\n▫️ بكره 2:00 م\n▫️ مواعيد تانية", Office.Application.Chatwoot.ChatwootText.Hazem(a));
        Assert.Equal("اختار ميعاد", Office.Application.Chatwoot.ChatwootText.Hazem(a with { Interactive = null }));
    }

    // ---------- fakes ----------

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
    private sealed class MemoryBooking(Func<DateTimeOffset> now) : IBookingStore
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
                Set(a => a.Id == hold.Id, AppointmentInfo.Expired);
            }

            var credit = Appointments.Where(a => a.PersonId == r.PersonId && a.Service == r.Service.Code
                                                  && a.State is AppointmentInfo.Expired or AppointmentInfo.CancelledByOffice
                                                  && Payments.TryGetValue(a.Id, out var p) && p.ProofAt is not null
                                                  && p.State is not (PaymentInfo.RefundRequested or PaymentInfo.Refunded))
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
    }
}
