using Office.Application.Texts;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.67 (the owner: «موافق»): a day and an hour typed under the list of times that is not the one booked gets a word why
/// before the free times: the hour is already past today, or that exact hour is not free.
/// </summary>
public sealed partial class BookingFlowTests
{
    private void ApproveTimeNotBookedTexts()
    {
        _texts[TextKeys.BookingTimePassed] = new ApprovedText(TextKeys.BookingTimePassed, 1, "الساعة {{time}} عدّت النهارده 🙏 دي أقرب المواعيد الفاضية 👇", 300, true);
        _texts[TextKeys.BookingTimeTaken] = new ApprovedText(TextKeys.BookingTimeTaken, 1, "الساعة {{time}} مش فاضية للأسف 🙏 دي أقرب المواعيد الفاضية 👇", 300, true);
    }

    [Fact]
    public async Task An_hour_already_past_today_is_said_before_the_free_times_from_now()
    {
        ApproveTimeNotBookedTexts();
        await ToTheOfficeTimes();
        var past = _now.AddMinutes(-41);
        _script["انهارده الساعه 4"] = new("other", null, "unknown", null, 0.85, AskedTime: Cairo(past), TimeExact: true);

        var d = await Say("انهارده الساعه 4");
        Assert.Equal([TextKeys.BookingTimePassed, TextKeys.BookingChooseSlot], Keys(d));
        Assert.Contains("عدّت النهارده", d.Actions[0].Text);
        Assert.All(Rows(d), t => Assert.True(t > _now));
        Assert.Empty(_booking.Appointments);
    }

    [Fact]
    public async Task An_exact_hour_that_is_not_free_is_said_before_the_free_times_from_it()
    {
        ApproveTimeNotBookedTexts();
        var rows = await ToTheOfficeTimes();
        var night = new DateTimeOffset(rows[^1].Date.AddDays(1).AddHours(3), rows[^1].Offset); // 3 at night: never open
        _script["بكرة الساعة 3 الفجر"] = new("other", null, "unknown", null, 0.85, AskedTime: Cairo(night), TimeExact: true);

        var d = await Say("بكرة الساعة 3 الفجر");
        Assert.Equal(TextKeys.BookingTimeTaken, Keys(d)[0]);
        Assert.Contains("مش فاضية", d.Actions[0].Text);
        Assert.Empty(_booking.Appointments);
    }

    [Fact]
    public async Task A_part_of_the_day_brings_the_times_alone_as_before()
    {
        ApproveTimeNotBookedTexts();
        var rows = await ToTheOfficeTimes();
        var from = rows[^1].AddHours(1);
        _script["بكره الصبح"] = new("other", null, "unknown", null, 0.9, AskedTime: Cairo(from), TimeExact: false);
        Assert.Equal([TextKeys.BookingChooseSlot], Keys(await Say("بكره الصبح")));
    }
}
