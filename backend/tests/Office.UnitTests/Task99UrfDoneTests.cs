using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Texts;
using Office.Application.Urf;
using Office.Application.Writing;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.99 (Ahmed Amin's chat, 2026-11-08, the owner: «تمام نفذ»): his contract's appointment was marked «خلص» by
/// itself and the worker sent the case back to picking a time, so Hazem never started a new request; and «عايز اعرف ميعاد
/// امتي» was read right but got «معلش، ممكن توضّحلي أكتر» once the reviewer refused the answer twice. Now an appointment
/// marked done closes its Urfi case (the worker: Task11ApiTests), the question about an appointment that passed is answered
/// from that appointment's approved words (they go when the written answer is refused), and the refused tries are kept.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string PastTime = "ميعاد حضرتك ({{service}}) كان {{day}} الساعة {{time}}، وعدّى خلاص.";

    private void PastTimeOn() =>
        _texts[TextKeys.SituationPast] = new ApprovedText(TextKeys.SituationPast, 1, PastTime, 300, true);

    /// <summary>situation_past as it is filled for the appointment that passed.</summary>
    private string PastTimeSaid()
    {
        var a = _booking.Appointments.Single(x => x.State is AppointmentInfo.Confirmed or AppointmentInfo.Completed);
        return $"ميعاد حضرتك ({BookingService.ByCode(a.Service)!.Name}) كان {BookingWords.Day(a.Start, _now)} الساعة {BookingWords.Time(a.Start)}، وعدّى خلاص.";
    }

    [Fact]
    public async Task A_passed_appointment_asked_about_gets_its_approved_words_when_the_written_answer_is_refused_twice()
    {
        CaseAnswersOn();
        FreeAnswerOn();
        PastTimeOn();
        await PassedUrfAppointment();
        _writer.Reply = "ميعاد حضرتك كان النهارده مع المستشار واتعمل العقد 🙏";
        _reviewer.Problem = ReplyReview.NotInFacts;
        var d = await Say(WhenWasIt);
        Assert.Equal([TextKeys.SituationPast], Keys(d));
        Assert.Equal(PastTimeSaid(), d.Actions[^1].Text); // never «معلش، ممكن توضّحلي أكتر»: he was understood
        var w = d.Effects!.Written!;
        Assert.Equal("review_not_in_facts", w.Guard);
        Assert.Equal(2, w.Refused.Count); // the first try and its retry, each with why (kept, never sent)
        Assert.All(w.Refused, r => Assert.Equal("review_not_in_facts", r.Problem));
        Assert.Equal(_writer.Reply, w.Refused[0].Text);
    }

    [Fact]
    public async Task A_passed_appointment_asked_about_is_answered_in_the_writer_s_words_from_its_approved_words()
    {
        CaseAnswersOn();
        FreeAnswerOn();
        PastTimeOn();
        await PassedUrfAppointment();
        _writer.Reply = "ميعاد حضرتك كان النهارده وعدّى خلاص 🙏";
        var d = await Say(WhenWasIt);
        Assert.Equal([TextKeys.SituationPast], Keys(d));
        Assert.Equal(_writer.Reply, d.Actions[^1].Text);
        Assert.Equal(WrittenReply.Passed, d.Effects!.Written!.Guard);
        Assert.Empty(d.Effects.Written.Refused);
        Assert.Contains(PastTimeSaid(), _writer.Last!.Approved); // what he asked is answered from that appointment's words
        Assert.Contains("آخر ميعاد لحضرتك", _writer.Last.Facts);
        Assert.Contains("ومفيش ميعاد تاني محجوز لحضرتك", _writer.Last.Facts); // never «محجوز ليه», read as «why?»
    }

    [Fact]
    public async Task Another_time_after_a_finished_urfi_contract_is_a_new_urfi_request_from_its_first_question()
    {
        CaseAnswersOn();
        FreeAnswerOn();
        await PassedUrfAppointment();
        // the worker (hazem.urf_due, v1.99): the appointment marked done closes the case with it
        var i = _booking.Appointments.FindIndex(x => x.State == AppointmentInfo.Confirmed);
        _booking.Appointments[i] = _booking.Appointments[i] with { State = AppointmentInfo.Completed };
        _booking.UrfCases[0] = _booking.UrfCases[0] with { State = UrfCase.Completed, AppointmentId = _booking.Appointments[i].Id };
        Assert.Equal([TextKeys.UrfAskAges], Keys(await Say(AnotherTime))); // not the office, and not the morning's answers
        Assert.Equal(UrfCase.Completed, _booking.UrfCases[0].State);
        Assert.Equal(UrfCase.Collecting, _booking.UrfCases[^1].State);
    }

    [Fact]
    public void An_urfi_case_closed_with_its_appointment_done_finishes_the_request()
    {
        var now = DateTimeOffset.UtcNow;
        var done = new RequestItem(RequestItem.Appointment, AppointmentInfo.Completed, true, now.AddHours(-5));
        var quiet = TimeSpan.FromDays(3);
        Assert.True(ReturningClient.Finished(new IntakeFacts(TimeSpan.FromHours(19), [new RequestItem(RequestItem.Urf, UrfCase.Completed, true), done]), now, quiet));
        // as before v1.99, a case sent back to picking a time kept the request open (Ahmed Amin's chat)
        Assert.False(ReturningClient.Finished(new IntakeFacts(TimeSpan.FromHours(19), [new RequestItem(RequestItem.Urf, UrfCase.DocumentsPending, true), done]), now, quiet));
    }

    [Fact]
    public void The_kind_of_a_booked_urfi_service_is_read_from_its_code()
    {
        Assert.Equal(UrfCase.Marriage, UrfCase.KindOf(BookingService.UrfMarriageOffice.Code));
        Assert.Equal(UrfCase.Marriage, UrfCase.KindOf(BookingService.UrfMarriageOwn.Code));
        Assert.Equal(UrfCase.Divorce, UrfCase.KindOf(BookingService.UrfDivorceOwn.Code));
        Assert.Null(UrfCase.KindOf(BookingService.Office.Code));
        Assert.Null(UrfCase.KindOf(null));
    }
}
