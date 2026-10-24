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

/// <summary>The booking in the conversation (B-08, B-09, B-15, B-22) with an in-memory store and calendar.</summary>
public sealed class BookingFlowTests
{
    private const string Number = "201000000088";
    private static readonly DateTimeOffset Now = AvailabilityTests.At(19, 12);

    private readonly Dictionary<string, ClientState> _rows = [];
    private readonly Dictionary<string, ApprovedText> _texts = [];
    private readonly MemoryBooking _booking = new();
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
        foreach (var (k, v) in HazemFlowTests.Approved)
        {
            _texts[k] = new ApprovedText(k, 1, v, 300, true);
        }

        foreach (var k in new[] { TextKeys.AskNameAgain, TextKeys.RescheduleOrCancel, TextKeys.AskTopic, TextKeys.Greeting })
        {
            _texts[k] = new ApprovedText(k, 1, "T:" + k, 300, true);
        }

        _texts[TextKeys.BookingConfirmed] = new ApprovedText(TextKeys.BookingConfirmed, 1,
            "✅ تم حجز {{service}} يوم {{day}} الساعة {{time}}. المبلغ {{amount}} جنيه، مقدمًا على InstaPay: {{instapay}}، أو محفظة كاش: {{wallet}}.", 600, true);
        foreach (var k in HazemResponder.BookingTextKeys.Where(k => k != TextKeys.BookingConfirmed))
        {
            var max = k.StartsWith("btn_", StringComparison.Ordinal) ? 20 : 300;
            _texts[k] = new ApprovedText(k, 1, k == TextKeys.BookingExisting ? "عندك {{service}} {{day}} {{time}}" : k == TextKeys.BookingRescheduled ? "اتغير {{day}} {{time}}" : "T:" + k[..Math.Min(k.Length, 16)], max, true);
        }
    }

    private HazemResponder Hazem => new(new Store(_rows), new Scripted(_script), new Texts(_texts), new NoLists(), new Clock(_now),
        new BookingDesk(_booking, _calendar));

    private async Task<TurnDecision> Send(IncomingMessage m)
    {
        var d = await Hazem.DecideAsync(new Turn($"wt:{++_turn}", Number, null, [m]), new HazemSettings(_settings), CancellationToken.None);
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
    public async Task A_time_is_booked_at_once_with_the_payment_details()
    {
        await ToTheOffer();
        var d = await Press(BookingChoice.Pick(BookingService.Office, AvailabilityTests.At(19, 15)));
        Assert.Equal([TextKeys.BookingConfirmed], Keys(d));
        var text = Assert.Single(d.Actions).Text!;
        Assert.Contains("مقابلة في المكتب يوم النهارده الساعة 3:00 م", text);
        Assert.Contains("1,000 جنيه", text);
        Assert.Contains("office@instapay", text);
        Assert.Contains("01000000000 (فودافون كاش)", text);
        var booked = Assert.Single(_booking.Appointments);
        Assert.Equal((AvailabilityTests.At(19, 15), AvailabilityTests.At(19, 15, 30), "inheritance"), (booked.Start, booked.End, booked.MatterType));
    }

    [Fact]
    public async Task Without_the_payment_details_the_booking_stands_and_the_office_gets_a_task()
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
    public async Task Urgent_shows_today_s_times_without_the_notice()
    {
        await ToTheOffer();
        _now = AvailabilityTests.At(19, 12, 3);
        _script["مستعجل جدا"] = new("urgent", null, "unknown", null, 0.95);
        var d = await Say("مستعجل جدا");
        Assert.Equal([TextKeys.UrgentPrice, TextKeys.BookingChooseSlot], Keys(d));
        Assert.Equal("النهارده 12:15 م", d.Actions[1].Interactive!.Choices[0].Title);
        Assert.Contains(PendingTask.UrgentRequest, TaskKinds(d));
    }

    [Fact]
    public async Task Change_once_then_the_lawyer_decides_and_cancel()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        var id = Assert.Single(_booking.Appointments).Id;

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
        Assert.Equal([TextKeys.BookingCancelled], Keys(cancelled));
        Assert.Equal([PendingTask.BookingCancelled], TaskKinds(cancelled));
        Assert.Equal(AppointmentInfo.CancelledByClient, _booking.Appointments[0].State);
    }

    [Fact]
    public async Task Less_than_12_hours_before_the_lawyer_decides()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(19, 20)));
        var id = _booking.Appointments[0].Id;
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
        _texts.Remove(TextKeys.BtnMoreSlots);
        var d = await ToTheOffer();
        Assert.DoesNotContain(d.Actions, a => a.Type == ActionType.SendInteractive);

        _texts[TextKeys.BtnMoreSlots] = new ApprovedText(TextKeys.BtnMoreSlots, 1, "مواعيد تانية", 24, true);
        _settings[BookingSettings.EnabledKey] = "false";
        _rows.Clear();
        d = await ToTheOffer();
        Assert.DoesNotContain(d.Actions, a => a.Type == ActionType.SendInteractive);
        Assert.Empty(_booking.Appointments);
    }

    [Fact]
    public async Task A_second_booking_of_the_same_service_shows_the_first()
    {
        await ToTheOffer();
        await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(20, 12)));
        var d = await Press(BookingChoice.Pick(BookingService.PhoneVideo, AvailabilityTests.At(21, 12)));
        Assert.Equal([TextKeys.BookingExisting], Keys(d));
        Assert.Single(_booking.Appointments);
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

    /// <summary>The same rules as the SQL functions of migration 0013, in memory.</summary>
    private sealed class MemoryBooking : IBookingStore
    {
        private readonly Dictionary<string, Guid> _byTurn = [];

        public Schedule Schedule { get; set; } = new(AvailabilityTests.Every(600, 1440), []);

        public List<AppointmentInfo> Appointments { get; } = [];

        public Task<Schedule> LoadScheduleAsync(DateOnly from, DateOnly to, CancellationToken ct) => Task.FromResult(Schedule);

        public Task<IReadOnlyList<AppointmentInfo>> ConfirmedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AppointmentInfo>>(Appointments.Where(a => a.State == AppointmentInfo.Confirmed && a.Start < to && a.End > from).ToList());

        public Task<AppointmentInfo?> UpcomingForAsync(Guid personId, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(Appointments.Where(a => a.PersonId == personId && a.State == AppointmentInfo.Confirmed && a.End > now).OrderBy(a => a.Start).FirstOrDefault());

        public Task<AppointmentInfo?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Appointments.FirstOrDefault(a => a.Id == id));

        public Task<BookOutcome> BookAsync(BookRequest r, CancellationToken ct)
        {
            if (_byTurn.TryGetValue(r.TurnId, out var existing))
            {
                return Task.FromResult(new BookOutcome(BookStatus.Exists, existing));
            }

            if (Appointments.FirstOrDefault(a => a.PersonId == r.PersonId && a.Service == r.Service.Code && a.State == AppointmentInfo.Confirmed) is { } same)
            {
                return Task.FromResult(new BookOutcome(BookStatus.AlreadyBooked, same.Id));
            }

            var a = new AppointmentInfo(r.Id, r.PersonId, r.Service.Code, r.Start, r.Start.AddMinutes(r.Service.Minutes), AppointmentInfo.Confirmed, 0,
                r.Service.Amount, r.MatterType);
            Appointments.Add(a);
            _byTurn[r.TurnId] = r.Id;
            return Task.FromResult(new BookOutcome(BookStatus.Booked, r.Id));
        }

        public Task<MoveStatus> MoveAsync(Guid id, DateTimeOffset start, DateTimeOffset end, string turnId, int bufferMinutes, CancellationToken ct)
        {
            var i = Appointments.FindIndex(a => a.Id == id);
            if (i < 0 || Appointments[i].Reschedules >= 1)
            {
                return Task.FromResult(i < 0 ? MoveStatus.NotFound : MoveStatus.NotAllowed);
            }

            Appointments[i] = Appointments[i] with { Start = start, End = end, Reschedules = Appointments[i].Reschedules + 1 };
            return Task.FromResult(MoveStatus.Moved);
        }

        public Task<bool> CancelAsync(Guid id, string state, CancellationToken ct)
        {
            var i = Appointments.FindIndex(a => a.Id == id && a.State == AppointmentInfo.Confirmed);
            if (i >= 0)
            {
                Appointments[i] = Appointments[i] with { State = state };
            }

            return Task.FromResult(i >= 0);
        }
    }
}
