using System.Globalization;
using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Domain.Booking;
using Office.Domain.Tasks;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.47 (B-32): Ahmed Amin's test chat («تمام بكره 10 وربع الصبح» under the list of times got the consultation offer
/// again). Words typed while buttons or rows wait are answered as the choice the AI reads in them; a day or a time on a list
/// of times is booked when free, or brings the free times from it; a later booking step never falls back to the offer.
/// </summary>
public sealed partial class BookingFlowTests
{
    private void ApproveTypedChoiceTexts()
    {
        foreach (var (k, v) in new Dictionary<string, string>
                 {
                     [TextKeys.BookingStepAgain] = "علشان نكمّل الحجز، اختار من اللي تحت 👇 أو اكتبلي اليوم والساعة اللي تناسب حضرتك.",
                     [TextKeys.UnclearAgain] = "تمام، هبلّغ المكتب برسالة حضرتك.",
                 })
        {
            _texts[k] = new ApprovedText(k, 1, v, 300, true);
        }
    }

    /// <summary>The new client up to the office's times on the screen; the rows' times.</summary>
    private async Task<List<DateTimeOffset>> ToTheOfficeTimes()
    {
        ApproveTypedChoiceTexts();
        await ToTheOffer();
        var list = await Press(BookingChoice.Mode(BookingService.Office));
        Assert.Equal([TextKeys.BookingChooseSlot], Keys(list));
        return Rows(list);
    }

    private static List<DateTimeOffset> Rows(TurnDecision d) => d.Actions[^1].Interactive!.Choices
        .Select(c => BookingChoice.Parse(c.Id)).Where(c => c is { Kind: 's' }).Select(c => c!.At!.Value).ToList();

    private static string Cairo(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, OfficeHours.Cairo).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_test_chat_a_time_typed_from_the_list_is_held_as_if_pressed_never_the_offer_again()
    {
        var rows = await ToTheOfficeTimes();
        _script["تمام بكره 10 وربع الصبح"] = new("consultation_request", "consultation_office", "office", null, 0.7, Picked: 2);

        var d = await Say("تمام بكره 10 وربع الصبح");
        Assert.Equal([TextKeys.BookingHold], Keys(d));
        Assert.Equal(rows[1], Assert.Single(_booking.Appointments).Start);
        Assert.DoesNotContain(TextKeys.ConsultationOffer, Keys(d));
    }

    [Fact]
    public async Task A_typed_kind_of_consultation_under_the_offer_is_its_button()
    {
        ApproveTypedChoiceTexts();
        await ToTheOffer();
        _script["عايز في المكتب"] = new("consultation_request", "consultation_office", "office", null, 0.9, Picked: 2);
        Assert.Equal([TextKeys.BookingChooseSlot], Keys(await Say("عايز في المكتب")));
    }

    [Fact]
    public async Task An_exact_time_not_on_the_list_is_held_when_it_is_free()
    {
        var rows = await ToTheOfficeTimes();
        var later = Rows(await Press(BookingChoice.More(BookingService.Office, rows[^1])))[0]; // free, not on the first list
        await Press(BookingChoice.Mode(BookingService.Office)); // the first list waits again
        _script["ممكن الميعاد ده"] = new("other", null, "unknown", null, 0.9, AskedTime: Cairo(later), TimeExact: true);

        Assert.Equal([TextKeys.BookingHold], Keys(await Say("ممكن الميعاد ده")));
        Assert.Equal(later, Assert.Single(_booking.Appointments).Start);
    }

    [Fact]
    public async Task A_day_or_a_part_of_the_day_brings_the_free_times_from_it()
    {
        var rows = await ToTheOfficeTimes();
        var from = rows[^1].AddHours(1);
        _script["بكره الصبح"] = new("other", null, "unknown", null, 0.9, AskedTime: Cairo(from), TimeExact: false);

        var d = await Say("بكره الصبح");
        Assert.Equal([TextKeys.BookingChooseSlot], Keys(d));
        Assert.All(Rows(d), t => Assert.True(t >= from));
        Assert.Empty(_booking.Appointments);
    }

    [Fact]
    public async Task Words_that_choose_nothing_under_the_times_bring_the_times_back_once_then_the_office_never_the_offer()
    {
        await ToTheOfficeTimes();
        _script["طيب انا عايز احجز"] = new("consultation_request", null, "unknown", null, 0.9);

        var again = await Say("طيب انا عايز احجز");
        Assert.Equal([TextKeys.BookingStepAgain, TextKeys.BookingChooseSlot], Keys(again));

        var third = await Say("طيب انا عايز احجز");
        Assert.Equal([TextKeys.UnclearAgain], Keys(third));
        Assert.Contains(PendingTask.UnclearMessage, TaskKinds(third));
    }

    [Fact]
    public async Task A_number_outside_the_choices_waiting_is_no_choice()
    {
        await ToTheOfficeTimes();
        _script["التاسع"] = new("other", null, "unknown", null, 0.9, Picked: 9);
        Assert.Empty(_booking.Appointments);
        Assert.DoesNotContain(TextKeys.BookingHold, Keys(await Say("التاسع")));
        Assert.Empty(_booking.Appointments);
    }
}
