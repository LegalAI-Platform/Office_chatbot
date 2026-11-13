using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Domain.Booking;
using Office.Domain.Tasks;

namespace Office.UnitTests;

/// <summary>v1.33 (B-09): the first moment the lawyer is free for an urgent call (off the slot grid, no notice).</summary>
public sealed class UrgentFirstFreeTests
{
    private static readonly TimeZoneInfo Z = OfficeHours.Cairo;
    private static readonly BookingRules R = BookingRules.Default; // 10 min buffer

    private static DateTimeOffset? First(DateTimeOffset now, BusyInterval[]? booked = null, BusyInterval[]? busy = null, BookingRules? rules = null,
        Schedule? schedule = null) =>
        Availability.FirstFree(schedule ?? new Schedule(AvailabilityTests.Every(600, 1440), []), rules ?? R, 15, booked ?? [], busy ?? [], now, Z);

    private static BusyInterval B(int h1, int m1, int h2, int m2, int day = 19) =>
        new(AvailabilityTests.At(day, h1, m1), AvailabilityTests.At(day, h2, m2));

    [Fact]
    public void Free_now_is_now_rounded_up_to_the_minute_off_the_grid()
    {
        Assert.Equal(AvailabilityTests.At(19, 12, 4), First(AvailabilityTests.At(19, 12, 3).AddSeconds(20)));
        Assert.Equal(AvailabilityTests.At(19, 12, 3), First(AvailabilityTests.At(19, 12, 3))); // a whole minute stays
    }

    [Fact]
    public void A_booking_or_a_calendar_event_in_the_way_moves_it_after_its_end_and_the_buffer()
    {
        Assert.Equal(AvailabilityTests.At(19, 12, 40), First(AvailabilityTests.At(19, 12, 5), booked: [B(12, 0, 12, 30)]));
        Assert.Equal(AvailabilityTests.At(19, 12, 40), First(AvailabilityTests.At(19, 12, 5), busy: [B(12, 0, 12, 30)]));
        // Without the buffer: right at the end.
        Assert.Equal(AvailabilityTests.At(19, 12, 30), First(AvailabilityTests.At(19, 12, 5), booked: [B(12, 0, 12, 30)], rules: R with { BufferMinutes = 0 }));
    }

    [Fact]
    public void A_gap_too_short_for_the_call_and_its_buffers_is_skipped()
    {
        // 12:30 → 12:45 leaves 15 minutes, but the buffers want 10 on each side: after 13:00 + 10.
        Assert.Equal(AvailabilityTests.At(19, 13, 10), First(AvailabilityTests.At(19, 12, 5), booked: [B(12, 0, 12, 30), B(12, 50, 13, 0)]));
        // Without the buffer the gap of exactly 15 minutes fits.
        Assert.Equal(AvailabilityTests.At(19, 12, 30),
            First(AvailabilityTests.At(19, 12, 5), booked: [B(12, 0, 12, 30), B(12, 45, 13, 0)], rules: R with { BufferMinutes = 0 }));
    }

    [Fact]
    public void After_closing_or_a_closed_day_it_is_the_next_opening()
    {
        Assert.Equal(AvailabilityTests.At(20, 10), First(AvailabilityTests.At(19, 23, 50))); // 15 minutes no longer fit before midnight
        Assert.Equal(AvailabilityTests.At(19, 10), First(AvailabilityTests.At(19, 7))); // before the opening: the opening, today
        var closed = new Schedule(AvailabilityTests.Every(600, 1440), [new DayException(new DateOnly(2026, 11, 19), ExceptionKind.ClosedDay, null)]);
        Assert.Equal(AvailabilityTests.At(20, 10), First(AvailabilityTests.At(19, 12), schedule: closed));
        // Today full until closing: tomorrow.
        Assert.Equal(AvailabilityTests.At(20, 10), First(AvailabilityTests.At(19, 12), busy: [B(11, 0, 23, 59)]));
    }

    [Fact]
    public void Nothing_within_the_horizon_is_null()
    {
        var never = new Schedule(AvailabilityTests.Every(600, 1440, Enum.GetValues<DayOfWeek>()), []);
        Assert.Null(First(AvailabilityTests.At(19, 12), schedule: never));
    }

    [Fact]
    public void V134_the_window_switch_counts_in_the_test_environment_only_and_is_off_by_default()
    {
        var on = new HazemSettings(new Dictionary<string, string> { [UrgentRules.AlertInWindowKey] = "true" });
        Assert.True(UrgentRules.From(on, testEnvironment: true).AlertInWindow);
        Assert.False(UrgentRules.From(on).AlertInWindow); // production: the template only, whatever the setting says
        Assert.False(UrgentRules.From(new HazemSettings(new Dictionary<string, string>()), testEnvironment: true).AlertInWindow);
        Assert.False(UrgentRules.Default.AlertInWindow);
    }

    [Fact]
    public void The_panel_numbers_and_the_words_of_the_alert()
    {
        Assert.Equal("201000000555", UrgentRules.Digits("0100 000 0555"));
        Assert.Equal("201000000555", UrgentRules.Digits("+201000000555"));
        Assert.Null(UrgentRules.Digits("abc"));
        Assert.Equal("01000000555", UrgentTexts.LocalNumber("201000000555"));
        Assert.Equal("+966500000000", UrgentTexts.LocalNumber("966500000000"));
        Assert.Equal("10", UrgentTexts.Opens(AvailabilityTests.At(20, 10)));
        Assert.Equal("10:30", UrgentTexts.Opens(AvailabilityTests.At(20, 10, 30)));
        Assert.Equal("عميل", UrgentTexts.Name(" ", null));
        Assert.Equal("Ahmed", UrgentTexts.Name(null, "Ahmed"));
        var id = Guid.NewGuid();
        Assert.Equal(new UrgentPress(UrgentPress.See, id), UrgentButtons.Parse(UrgentButtons.See(id)));
        Assert.Equal(new UrgentPress(UrgentPress.Ok, id), UrgentButtons.Parse(UrgentButtons.Ok(id)));
        Assert.Equal(new UrgentPress(UrgentPress.Bad, id), UrgentButtons.Parse(UrgentButtons.Bad(id)));
        Assert.Null(UrgentButtons.Parse("ug:ack:" + id.ToString("N")));
        Assert.True(UrgentButtons.See(id).Length <= 200 && UrgentButtons.Bad(id).Length <= 200);
    }
}

/// <summary>v1.33 (B-09): the urgent consultation as a paid call-back, in the conversation.</summary>
public sealed partial class BookingFlowTests
{
    private IClientLists _lists = new NoLists();
    private readonly FakeUrgentCalls _acks = new();

    private void UrgentOn()
    {
        var t = new Dictionary<string, string>
        {
            [TextKeys.UrgentPay] = "ادفع {{amount}} {{instapay}} {{wallet}}",
            [TextKeys.UrgentNight] = "من الساعة {{opens}} الصبح",
            [TextKeys.UrgentNow] = "خلال 10 دقايق",
            [TextKeys.UrgentAfter] = "حوالي الساعة {{time}}",
            [TextKeys.UrgentLater] = "هيكلم حضرتك {{day}} الساعة {{time}}", // v1.42
            [TextKeys.UrgentSeen] = "شاف",
            [TextKeys.UrgentWaiting] = "بنوصّل",
            [TextKeys.UrgentAlert] = "🚨 {{name}} {{phone}} {{time}}",
            [TextKeys.BtnUrgentSee] = "شوف التحويل",
            [TextKeys.BtnUrgentOk] = "التحويل تمام، هكلمه",
            [TextKeys.BtnUrgentBad] = "فيه مشكلة",
        };
        foreach (var (k, v) in t)
        {
            _texts[k] = new ApprovedText(k, 1, v, k.StartsWith("btn_", StringComparison.Ordinal) ? 20 : 300, true);
        }
    }

    /// <summary>
    /// «السلام عليكم» (the welcome), then the services list's urgent row. v1.35: the name is asked first (as at any booking);
    /// this answers it and returns that turn, where the call is held and the payment told (the 1.32 path returns the row's).
    /// </summary>
    private async Task<TurnDecision> UrgentRow()
    {
        var row = await UrgentPressed();
        if (!Keys(row).Contains(TextKeys.AskNameBooking))
        {
            return row;
        }

        _script["محمد أحمد"] = new("provide_name", null, "unknown", "محمد أحمد", 0.95);
        return await Say("محمد أحمد");
    }

    /// <summary>«السلام عليكم», then the urgent row itself (v1.35: its price and the name question).</summary>
    private async Task<TurnDecision> UrgentPressed()
    {
        _script["السلام عليكم"] = new("greeting", null, "unknown", null, 0.9);
        var t = _now;
        _now = Now.AddHours(-1);
        await Say("السلام عليكم");
        _now = t;
        return await Press("sv:urgent");
    }

    private AppointmentInfo TheCall => Assert.Single(_booking.Appointments, a => a.Service == BookingService.Urgent.Code && a.IsOpen);

    [Fact]
    public async Task Urgent_with_the_booking_on_holds_the_first_free_moment_and_says_where_to_pay()
    {
        UrgentOn();
        var row = await UrgentPressed();
        Assert.Equal([TextKeys.UrgentPrice, TextKeys.AskNameBooking], Keys(row)); // v1.35: the name first, nothing held yet
        Assert.DoesNotContain(PendingTask.UrgentRequest, TaskKinds(row)); // v1.38: the office's urgent task waits for the payment
        Assert.Empty(_booking.Appointments);
        _script["محمد أحمد"] = new("provide_name", null, "unknown", "محمد أحمد", 0.95);
        var d = await Say("محمد أحمد");
        Assert.Equal([TextKeys.UrgentPay], Keys(d));
        Assert.All(d.Actions, a => Assert.Null(a.Interactive)); // no list of times
        Assert.Equal("ادفع 1,000 office@instapay 01000000000 (فودافون كاش)", d.Actions[0].Text);
        Assert.Equal("محمد أحمد", _rows[Number].Person.Name?.Raw);
        var call = TheCall;
        Assert.Equal((AppointmentInfo.PendingPayment, Now, Now.AddMinutes(15), CallApp.WhatsApp), (call.State, call.Start, call.End, call.CallApp));
        Assert.Equal(Now.AddMinutes(60), call.HoldUntil); // the short hold (booking.hold_short_minutes)
    }

    [Fact]
    public async Task V135_a_photo_instead_of_the_name_holds_the_call_at_once_without_asking_again()
    {
        UrgentOn();
        await UrgentPressed();
        var photo = await Send(new IncomingMessage($"wamid.{++_turn}", MessageType.Image, null, null, null,
            Media: new MediaRef(MediaStatus.Stored, "office-sensitive", $"k{_turn}", "image/jpeg", 1000)));
        Assert.Equal([TextKeys.UrgentPay], Keys(photo)); // no «لو سمحت، ممكن اسم حضرتك؟» for someone in a hurry
        Assert.Equal(AppointmentInfo.PendingPayment, TheCall.State);
        Assert.Null(_rows[Number].Person.Name);
    }

    [Fact]
    public async Task V135_a_name_already_known_is_not_asked_again()
    {
        UrgentOn();
        await UrgentRow(); // the name the first time
        _booking.Appointments.Clear();
        var again = await Press("sv:urgent");
        Assert.Equal([TextKeys.UrgentPrice, TextKeys.UrgentPay], Keys(again));
    }

    [Fact]
    public async Task At_night_it_says_when_the_lawyer_answers_and_holds_that_first_moment()
    {
        UrgentOn();
        _now = AvailabilityTests.At(19, 23, 55);
        var d = await UrgentRow();
        Assert.Equal([TextKeys.UrgentNight, TextKeys.UrgentPay], Keys(d)); // after the name (v1.35)
        Assert.Equal("من الساعة 10 الصبح", d.Actions[0].Text);
        Assert.Equal(AvailabilityTests.At(20, 10), TheCall.Start);
        Assert.Equal(AvailabilityTests.At(20, 10), TheCall.HoldUntil); // held until the lawyer's first moment itself
    }

    [Fact]
    public async Task The_receipt_while_the_lawyer_is_free_books_now_and_says_within_10_minutes()
    {
        UrgentOn();
        await UrgentRow();
        _now = Now.AddMinutes(7).AddSeconds(30);
        var d = await Receipt();
        Assert.Equal([TextKeys.UrgentNow], Keys(d));
        Assert.Contains(PendingTask.UrgentRequest, TaskKinds(d)); // v1.38: the office's urgent task once he has paid
        Assert.Equal((AppointmentInfo.Confirmed, Now.AddMinutes(8)), (TheCall.State, TheCall.Start)); // now, rounded up to the minute
        Assert.Equal(PaymentInfo.ProofReceived, _booking.Payments[TheCall.Id].State); // the review stays the panel's
    }

    [Theory]
    [InlineData(AppointmentInfo.PendingPayment, false)] // a normal booking held and not paid yet is busy
    [InlineData(AppointmentInfo.Confirmed, false)]
    [InlineData(null, true)]                             // an event in the lawyer's calendar
    public async Task The_receipt_while_the_lawyer_is_busy_says_about_the_end_of_what_is_in_the_way(string? state, bool calendar)
    {
        UrgentOn();
        _settings[BookingSettings.BufferKey] = "0";
        await UrgentRow(); // held at 12:00
        _now = Now.AddMinutes(2);
        var busy = new BusyInterval(Now.AddMinutes(1), Now.AddMinutes(40)); // 12:01 → 12:40 (an appointment booked off the grid by the panel)
        if (calendar)
        {
            _calendar.Busy.Add(busy);
        }
        else
        {
            _booking.Appointments.Add(new AppointmentInfo(Guid.NewGuid(), Guid.NewGuid(), BookingService.Office.Code, busy.Start, busy.End, state!, 0, 1000,
                null, state == AppointmentInfo.PendingPayment ? Now.AddHours(1) : null));
        }

        var d = await Receipt();
        Assert.Equal([TextKeys.UrgentAfter], Keys(d));
        Assert.Equal("حوالي الساعة 12:40 م", Assert.Single(d.Actions).Text);
        Assert.Equal(Now.AddMinutes(40), TheCall.Start);
    }

    [Fact]
    public async Task The_call_not_today_gets_its_own_receipt_text_with_the_day()
    {
        UrgentOn();
        _now = AvailabilityTests.At(19, 23, 55);
        await UrgentRow();
        _now = AvailabilityTests.At(19, 23, 57);
        var d = await Receipt();
        Assert.Equal([TextKeys.UrgentLater], Keys(d)); // v1.42: never «هبعتلك التأكيد» for an urgent call
        Assert.Equal(AvailabilityTests.At(20, 10), TheCall.Start);
    }

    [Fact]
    public async Task V142_at_half_past_one_at_night_he_hears_the_morning_before_paying_and_never_that_the_lawyer_is_in_a_call()
    {
        // Abu El-Leil (2026-11-01): 01:30 and the call at 10:00 are the same date; the lawyer is asleep, not in a call.
        UrgentOn();
        _now = AvailabilityTests.At(20, 1, 30);
        var d = await UrgentRow();
        Assert.Equal([TextKeys.UrgentNight, TextKeys.UrgentPay], Keys(d)); // the morning, before he pays
        Assert.Equal("من الساعة 10 الصبح", d.Actions[0].Text);
        Assert.Equal(AvailabilityTests.At(20, 10), TheCall.Start);

        _now = AvailabilityTests.At(20, 1, 31);
        var paid = await Receipt();
        Assert.Equal([TextKeys.UrgentLater], Keys(paid)); // not urgent_after («المستشار في مكالمة دلوقتي»)
        Assert.Equal("هيكلم حضرتك النهارده الساعة 10:00 ص", Assert.Single(paid.Actions).Text);
        Assert.Contains(PendingTask.UrgentRequest, TaskKinds(paid));
    }

    [Fact]
    public async Task V142_his_urgent_words_after_paying_make_no_second_task()
    {
        UrgentOn();
        _now = AvailabilityTests.At(20, 1, 30);
        await UrgentRow();
        _now = AvailabilityTests.At(20, 1, 31);
        await Receipt(); // the one urgent task, with the receipt
        _now = AvailabilityTests.At(20, 1, 33);
        _script["خليه يكلمني ضروري ناو"] = new("urgent", null, "unknown", null, 0.95);
        Assert.DoesNotContain(PendingTask.UrgentRequest, TaskKinds(await Say("خليه يكلمني ضروري ناو"))); // one bell, one e-mail
    }

    [Fact]
    public async Task A_late_receipt_after_the_hold_ran_out_is_still_booked_from_now()
    {
        UrgentOn();
        await UrgentRow();
        _booking.Expire();
        _now = Now.AddHours(2);
        var d = await Receipt();
        Assert.Equal([TextKeys.UrgentNow], Keys(d));
        Assert.Equal((AppointmentInfo.Confirmed, Now.AddHours(2)), (TheCall.State, TheCall.Start));
    }

    [Fact]
    public async Task Without_the_office_s_payment_details_urgent_pay_is_not_sent_and_the_office_gets_a_task()
    {
        UrgentOn();
        _settings.Remove(OfficeInfo.InstaPayKey);
        var d = await UrgentRow();
        Assert.Empty(Keys(d)); // the price was said with the name question (v1.35)
        Assert.Contains(PendingTask.BookingMessageFailed, TaskKinds(d));
        Assert.Equal(AppointmentInfo.PendingPayment, TheCall.State); // the time is still held
    }

    [Fact]
    public async Task No_calendar_is_the_booking_s_unavailable_answer()
    {
        UrgentOn();
        _calendar.Down = true;
        var d = await UrgentRow();
        Assert.Equal([TextKeys.BookingUnavailable], Keys(d)); // after the name (v1.35)
        Assert.Contains(PendingTask.BookingUnavailable, TaskKinds(d));
        Assert.Empty(_booking.Appointments);
    }

    [Fact]
    public async Task With_one_text_switched_off_or_the_booking_off_urgent_is_as_in_1_32()
    {
        UrgentOn();
        _texts[TextKeys.UrgentSeen] = _texts[TextKeys.UrgentSeen] with { Active = false };
        var list = await UrgentRow();
        Assert.Equal([TextKeys.UrgentPrice, TextKeys.BookingChooseSlot], Keys(list)); // today's times to pick from

        _texts[TextKeys.UrgentSeen] = _texts[TextKeys.UrgentSeen] with { Active = true };
        _settings[BookingSettings.EnabledKey] = "false";
        var off = await Press("sv:urgent");
        Assert.Equal([TextKeys.UrgentPrice, TextKeys.ConsultationOffer], Keys(off));
        Assert.Empty(_booking.Appointments); // nothing held on either way
    }

    [Fact]
    public async Task The_alert_s_buttons_are_read_by_code_from_any_number_and_answered_by_the_urgent_desk_only()
    {
        _lists = new LawyerList();
        var id = Guid.NewGuid();
        foreach (var payload in new[] { UrgentButtons.See(id), UrgentButtons.Ok(id), UrgentButtons.Ok(id) })
        {
            // The template's quick reply comes as a button; the lawyer's number is on the lawyer's list and no test recipient.
            var d = await Send(new Turn($"wt:{++_turn}", "201222222222", null,
                [new IncomingMessage($"wamid.{++_turn}", MessageType.Button, null, null, null, Button: new Button(payload, "x"))]));
            Assert.Empty(d.Actions);
            Assert.Null(d.Effects); // no AI, no state, no reply in the turn
        }

        Assert.Equal([(UrgentPress.See, id), (UrgentPress.Ok, id), (UrgentPress.Ok, id)], _acks.Presses.Select(p => (p.Press.Kind, p.Press.Id)));
        Assert.All(_acks.Presses, p => Assert.Equal("201222222222", p.WaId));
    }

    private sealed class LawyerList : IClientLists
    {
        public Task<ListMatch> FindAsync(string waId, CancellationToken ct) => Task.FromResult(new ListMatch(false, true, null));
    }

    private sealed class FakeUrgentCalls : IUrgentCalls
    {
        public List<(UrgentPress Press, string WaId)> Presses { get; } = [];

        public Task<bool> PressAsync(UrgentPress press, string presserWaId, string turnId, CancellationToken ct)
        {
            Presses.Add((press, presserWaId));
            return Task.FromResult(Presses.Count(p => p.Press == press) == 1);
        }
    }
}
