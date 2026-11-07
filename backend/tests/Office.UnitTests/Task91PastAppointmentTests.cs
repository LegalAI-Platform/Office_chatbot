using Office.Application.Booking;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Urf;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.91 (Ahmed Amin's chat, 2026-11-07, the owner: «لو ده حل عام فابدا حل كل المشاكل دي»): his Urfi time had passed.
/// «هو الميعاد كان امتي» got the welcome, «السلام عليكم» got «قبل الميعاد», «عايز ميعاد تاني» went to the office, and «عايز اتجوز
/// عرفي» under the services list took the row «جواز أو طلاق عرفي» and got «تحت أمرك، قولّي المطلوب». Now the appointment that passed
/// is read with everything else, and words that pick a row keep what the AI read in them. Switch off: as v1.81.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string WhenWasIt = "هو الميعاد كان امتي";
    private const string AnotherTime = "انا كان ليا ميعاد انهارده وعايز اخد ميعاد تاني";

    /// <summary>The Urfi questions answered, a time picked and paid, and that time passed an hour ago.</summary>
    private async Task PassedUrfAppointment()
    {
        await ToTheGuardian();
        await Answer(UrfStep.Guardian, "brother");
        var times = await Answer(UrfStep.Witnesses, "own");
        await Press(times.Actions.Single().Interactive!.Choices[0].Id);
        await Receipt();
        var a = Assert.Single(_booking.Appointments, x => x.State == AppointmentInfo.Confirmed);
        _now = a.End.AddHours(1);
        // the worker (hazem.urf_due): a booked case whose appointment is no longer held or confirmed goes back to picking a time
        _booking.UrfCases[0] = _booking.UrfCases[0] with { State = UrfCase.DocumentsPending, AppointmentId = null };
        _script[WhenWasIt] = new("my_status", "urf_marriage", "unknown", null, 0.9);
        _script[AnotherTime] = new("reschedule_or_cancel", "urf_marriage", "unknown", null, 0.9);
    }

    [Fact]
    public async Task When_his_time_passed_his_question_about_it_is_answered_from_the_facts()
    {
        CaseAnswersOn();
        FreeAnswerOn();
        await PassedUrfAppointment();
        _writer.Reply = "ميعاد حضرتك كان النهارده الساعة 2:00 م 🙏";
        var d = await Say(WhenWasIt);
        Assert.Equal([TextKeys.FreeAnswer], Keys(d));
        Assert.Contains("آخر ميعاد ليه", _writer.Last!.Facts);
        Assert.Contains("وعدّى خلاص، ومفيش ميعاد جاي", _writer.Last.Facts); // never «قبل الميعاد»
    }

    [Fact]
    public async Task When_his_time_passed_another_time_for_his_open_urfi_request_gets_its_free_times()
    {
        CaseAnswersOn();
        FreeAnswerOn();
        await PassedUrfAppointment();
        Assert.Equal([TextKeys.UrfChooseSlot], Keys(await Say(AnotherTime))); // not the office: his answers are all there
    }

    [Fact]
    public async Task With_the_switch_off_a_passed_time_is_as_v1_81()
    {
        UrfFactsOn(); // understanding v2 on, hazem.urf_case_said off
        GuardianCanOn();
        FreeAnswerOn();
        await PassedUrfAppointment();
        Assert.DoesNotContain(TextKeys.FreeAnswer, Keys(await Say(WhenWasIt)));
        Assert.Equal([TextKeys.RescheduleOrCancel], Keys(await Say(AnotherTime)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Words_that_pick_the_urfi_row_keep_the_marriage_the_ai_read(bool switchOn)
    {
        if (switchOn)
        {
            CaseAnswersOn();
        }
        else
        {
            UrfFactsOn();
        }

        UrfOn();
        ServicesOn();
        await ToTheOffer();
        var menu = await Press(ServiceRow.Id(ServiceRow.Menu));
        var rows = menu.Actions[^1].Interactive!.Choices.Select(c => c.Id).ToList();
        _script["عايز اتجوز عرفي"] = new("service_by_name", "urf_marriage", "unknown", null, 0.9, Picked: rows.IndexOf(ServiceRow.Id(ServiceRow.Urf)) + 1);
        var d = await Say("عايز اتجوز عرفي");
        Assert.Equal([switchOn ? TextKeys.UrfAskAges : TextKeys.UrfUnclear], Keys(d)); // off: «تحت أمرك، قولّي المطلوب», as v1.81
    }
}
