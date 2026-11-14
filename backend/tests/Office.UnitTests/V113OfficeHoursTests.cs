using Office.Application.Booking;
using Office.Application.Responders;
using Office.Domain.Booking;
using Office.Domain.Tasks;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.113 (the owner, 2026-11-14: «انا عايز الاستشارات تبقى لغايه 12 بالليل عادي. المقابلات في المكتب من 10 الصبح ل 8
/// بالليل» … «تمام نفّذ»). The panel's week stays the consultations' hours (every day 10:00–24:00 on the server). The office's
/// meetings and the Urfi signings keep inside the office's hours of the day (booking.office_*), after the week and the exceptions.
/// The phone, video and urgent calls never do.
/// </summary>
public sealed class V113OfficeHoursTests
{
    private static readonly TimeZoneInfo Z = OfficeHours.Cairo;
    private static readonly MinuteRange TenToEight = new(600, 1200);

    private static DateOnly D(int day) => new(2026, 11, day);

    private static List<string> Times(Schedule s, int minutes, int day) =>
        Availability.Find(s, BookingRules.Default, new SlotQuery(minutes, AvailabilityTests.At(day - 1, 9), false, D(day), 500), [], [],
                AvailabilityTests.At(day - 1, 9), Z)
            .Select(x => TimeZoneInfo.ConvertTime(x.Start, Z).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)).ToList();

    [Fact]
    public void An_office_meeting_ends_by_eight_and_a_phone_call_goes_on_until_midnight()
    {
        var week = new Schedule(AvailabilityTests.Every(600, 1440), []);

        var office = Times(week.Within(TenToEight), 30, 20);
        Assert.Equal(("10:00", "19:30"), (office[0], office[^1])); // the last meeting starts at 7:30 and ends at 8
        var urf = Times(week.Within(TenToEight), 60, 20);
        Assert.Equal(("10:00", "19:00"), (urf[0], urf[^1])); // an hour (booking.urf_minutes)
        var phone = Times(week, 15, 20);
        Assert.Equal(("10:00", "23:45"), (phone[0], phone[^1]));
    }

    [Fact]
    public void The_office_hours_cut_a_day_s_own_hours_too_and_a_closed_day_stays_closed()
    {
        var custom = new Schedule(AvailabilityTests.Every(600, 1440),
            [new DayException(D(20), ExceptionKind.CustomHours, new MinuteRange(18 * 60, 22 * 60)), new DayException(D(21), ExceptionKind.ClosedDay, null)]);
        Assert.Equal(["18:00", "18:15", "18:30", "18:45", "19:00", "19:15", "19:30"], Times(custom.Within(TenToEight), 30, 20));
        Assert.Empty(Times(custom.Within(TenToEight), 30, 21));
        Assert.Equal([new MinuteRange(18 * 60, 22 * 60)], custom.HoursOn(D(20))); // the schedule itself is not changed
    }

    [Theory]
    [InlineData("consultation_office", true)]
    [InlineData("urf_marriage_office", true)]
    [InlineData("urf_divorce_own", true)]
    [InlineData("consultation_phone_video", false)]
    [InlineData("consultation_urgent", false)]
    public void Only_the_office_s_appointments_keep_the_office_hours(string service, bool inside)
    {
        var booking = BookingSettings.From(new HazemSettings(new Dictionary<string, string>
        {
            [BookingSettings.OfficeStartKey] = "600",
            [BookingSettings.OfficeEndKey] = "1200",
        }));
        var week = new Schedule(AvailabilityTests.Every(600, 1440), []);
        Assert.Equal(inside ? TenToEight : null, booking.ScheduleFor(BookingService.ByCode(service)!, week).Window);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("600", "")]
    [InlineData("1200", "600")]
    [InlineData("600", "1500")]
    [InlineData("ten", "eight")]
    public void Unset_or_broken_office_hours_leave_the_panel_s_hours(string start, string end) =>
        Assert.Null(BookingSettings.From(new HazemSettings(new Dictionary<string, string>
        {
            [BookingSettings.OfficeStartKey] = start,
            [BookingSettings.OfficeEndKey] = end,
        })).OfficeWindow);

    [Fact]
    public void The_hours_say_the_office_s_meetings_after_the_consultations_once()
    {
        var week = AvailabilityTests.Every(600, 1440);
        Assert.Equal("كل يوم من 10:00 ص لـ 12 بالليل، والمقابلات في المكتب من 10:00 ص لـ 8:00 م", BookingWords.Hours(week, TenToEight));
        Assert.Equal("كل يوم من 10:00 ص لـ 12 بالليل", BookingWords.Hours(week, null));
        Assert.Equal("كل يوم من 10:00 ص لـ 12 بالليل", BookingWords.Hours(week, new MinuteRange(0, 1440))); // nothing shorter to say
        Assert.Null(BookingWords.Hours(WeeklyHours.Create([]).Hours!, TenToEight)); // no day open: nothing to say yet
    }
}

/// <summary>v1.113: the same from WhatsApp (the flow of <see cref="BookingFlowTests"/>).</summary>
public sealed partial class BookingFlowTests
{
    [Fact]
    public async Task V113_an_office_meeting_is_offered_inside_the_office_hours_only()
    {
        _settings[BookingSettings.OfficeStartKey] = "1140"; // 19:00
        _settings[BookingSettings.OfficeEndKey] = "1200"; // 20:00: the last meeting of the day starts at 19:30
        await ToTheOffer();

        var office = Assert.Single((await Press(BookingChoice.Mode(BookingService.Office))).Actions).Interactive!;
        Assert.Equal(
            new[] { AvailabilityTests.At(19, 19), AvailabilityTests.At(19, 19, 15), AvailabilityTests.At(19, 19, 30), AvailabilityTests.At(20, 19) }
                .Select(t => BookingChoice.Pick(BookingService.Office, t)),
            office.Choices.Select(c => c.Id).Where(id => id.StartsWith("bk:s:", StringComparison.Ordinal)));
    }
}
