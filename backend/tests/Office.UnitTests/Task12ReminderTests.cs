using Office.Application.Booking;
using Office.Application.Reminders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>TASK 12 (DECISIONS v1.16, B-18): the reminders' timing, their buttons and what they say. Cairo time, November 2026.</summary>
public sealed class ReminderRulesTests
{
    private static DateTimeOffset At(int day, int h, int m = 0) => AvailabilityTests.At(day, h, m);

    [Fact]
    public void The_day_before_is_24_hours_before_and_two_hours_is_two_hours_before()
    {
        var now = At(19, 12);
        Assert.Equal(ReminderPlan.At(At(20, 15)), ReminderRules.Plan(ReminderKinds.DayBefore, At(21, 15), now));
        Assert.Equal(ReminderPlan.At(At(21, 13)), ReminderRules.Plan(ReminderKinds.TwoHours, At(21, 15), now));
    }

    [Fact]
    public void Between_midnight_and_ten_it_waits_for_ten()
    {
        var now = At(19, 12);
        Assert.Equal(ReminderPlan.At(At(20, 10)), ReminderRules.Plan(ReminderKinds.DayBefore, At(21, 9), now));
        Assert.Equal(ReminderPlan.At(At(21, 10)), ReminderRules.Plan(ReminderKinds.TwoHours, At(21, 11, 30), now)); // 9:30 → 10:00
        Assert.Equal(ReminderPlan.At(At(20, 10)), ReminderRules.Plan(ReminderKinds.DayBefore, At(21, 0), now));  // a midnight appointment
    }

    [Fact]
    public void Under_an_hour_before_after_waiting_for_ten_it_is_never_sent()
    {
        var now = At(19, 12);
        Assert.Equal(ReminderPlan.Skipped(ReminderSkips.TooClose), ReminderRules.Plan(ReminderKinds.TwoHours, At(21, 10, 30), now));
        Assert.Equal(ReminderPlan.Skipped(ReminderSkips.TooClose), ReminderRules.Plan(ReminderKinds.TwoHours, At(21, 10, 59), now));
        Assert.Equal(ReminderPlan.At(At(21, 10)), ReminderRules.Plan(ReminderKinds.TwoHours, At(21, 11), now));
        // «النهارده»: two hours before a 1:00 appointment would be 11 at night the day before: never sent.
        Assert.Equal(ReminderPlan.Skipped(ReminderSkips.TooClose), ReminderRules.Plan(ReminderKinds.TwoHours, At(21, 1), now));
    }

    [Fact]
    public void Confirmed_after_its_time_it_is_never_sent()
    {
        var now = At(19, 13); // the receipt came 23 hours before a 12:00 appointment tomorrow
        Assert.Equal(ReminderPlan.Skipped(ReminderSkips.Passed), ReminderRules.Plan(ReminderKinds.DayBefore, At(20, 12), now));
        Assert.Equal(ReminderPlan.At(At(20, 10)), ReminderRules.Plan(ReminderKinds.TwoHours, At(20, 12), now));
        Assert.Equal(ReminderPlan.Skipped(ReminderSkips.Passed), ReminderRules.Plan(ReminderKinds.TwoHours, At(19, 14, 30), now));
    }

    [Fact]
    public void Sent_late_only_while_its_words_are_still_true()
    {
        Assert.True(ReminderRules.StillUseful(ReminderKinds.DayBefore, At(21, 12), At(20, 23)));
        Assert.False(ReminderRules.StillUseful(ReminderKinds.DayBefore, At(21, 12), At(21, 0, 30))); // «بكرة» would be wrong
        Assert.True(ReminderRules.StillUseful(ReminderKinds.TwoHours, At(21, 12), At(21, 10, 59)));
        Assert.False(ReminderRules.StillUseful(ReminderKinds.TwoHours, At(21, 12), At(21, 11, 30))); // under an hour
        Assert.False(ReminderRules.StillUseful(ReminderKinds.TwoHours, At(21, 12), At(20, 22))); // «النهارده» would be wrong
    }

    [Fact]
    public void The_buttons_are_read_without_the_ai_and_carry_the_time()
    {
        var id = Guid.NewGuid();
        var attend = BookingChoice.Parse(ReminderButtons.Attend(id, At(21, 12)))!;
        Assert.Equal(('a', id, (DateTimeOffset?)At(21, 12)), (attend.Kind, attend.AppointmentId!.Value, attend.At));
        var change = BookingChoice.Parse(ReminderButtons.Change(id))!;
        Assert.Equal(('e', id), (change.Kind, change.AppointmentId!.Value));
        Assert.Null(BookingChoice.Parse($"bk:a:{id:N}")); // «هحضر» always names its time
        Assert.True(ReminderButtons.Attend(id, At(21, 12)).Length <= 200);
    }

    [Fact]
    public void The_date_after_bukra_is_never_a_relative_word()
    {
        Assert.Equal("الجمعة 20/11", BookingWords.Date(At(20, 12)));
        Assert.Equal("بكرة", BookingWords.Day(At(20, 12), At(19, 12))); // the flow's own words stay as they were
    }

    [Fact]
    public void The_details_line_is_the_office_the_call_or_who_comes()
    {
        var office = new OfficeInfo("", "", "", "١٢ شارع التحرير، الدقي", "https://maps.example/x");
        var (key, fill) = ReminderTexts.Details(BookingService.Office, office, null);
        Assert.Equal(TextKeys.ReminderDetailsOffice, key);
        Assert.Equal(("١٢ شارع التحرير، الدقي", "https://maps.example/x"), (fill["address"], fill["map"]));
        Assert.Equal(TextKeys.ReminderDetailsPhone, ReminderTexts.Details(BookingService.PhoneVideo, office, null).Key);
        Assert.Equal(TextKeys.ReminderDetailsPhone, ReminderTexts.Details(BookingService.Urgent, office, null).Key);
        var urf = ReminderTexts.Details(BookingService.UrfMarriageOwn, office, UrfCase.AttendeesFor(UrfCase.Marriage, "own"));
        Assert.Equal((TextKeys.ReminderDetailsUrf, "الزوج والزوجة والولي والشاهدين"), (urf.Key, urf.Fill["attendees"]));
        Assert.Equal("الطرفين", UrfCase.AttendeesFor("divorce", "office"));

        // No address written yet (P1): the slot stays open, so the text is never sent half-written.
        var (k, missing) = ReminderTexts.Details(BookingService.Office, new OfficeInfo("", "", "", " ", ""), null);
        var text = new ApprovedText(k, 1, "المكتب في {{address}}، واللوكيشن: {{map}}", 300, true);
        Assert.Equal(TextRenderer.MissingValue, TextRenderer.Render(text, missing).Reason);
    }

    [Fact]
    public void The_calendar_event_shows_i_will_come_for_the_current_time_only()
    {
        var a = new Office.Infrastructure.Persistence.AppointmentRow
        {
            Service = BookingService.PhoneVideo.Code, State = AppointmentInfo.Confirmed, BookedTurnId = "wt:1", CalendarState = "synced",
            StartsAt = At(20, 12), EndsAt = At(20, 12, 15),
        };
        Assert.DoesNotContain("الحضور", Office.Infrastructure.Booking.CalendarSync.Summary(a, "س", "201000000001", null));
        a.AttendanceFor = At(20, 12);
        Assert.EndsWith(" – الحضور: ✅ أكّد", Office.Infrastructure.Booking.CalendarSync.Summary(a, "س", "201000000001", null));
        a.StartsAt = At(20, 13); // moved after he pressed: not confirmed for the new time
        Assert.DoesNotContain("الحضور", Office.Infrastructure.Booking.CalendarSync.Summary(a, "س", "201000000001", null));
    }

    [Fact]
    public void The_template_and_the_text_of_each_kind()
    {
        Assert.Equal(("reminder_day_before", TextKeys.ReminderDayBefore), (ReminderTexts.Template(ReminderKinds.DayBefore), ReminderTexts.TextKey(ReminderKinds.DayBefore)));
        Assert.Equal(("reminder_2h_before", TextKeys.Reminder2hBefore), (ReminderTexts.Template(ReminderKinds.TwoHours), ReminderTexts.TextKey(ReminderKinds.TwoHours)));
    }
}
