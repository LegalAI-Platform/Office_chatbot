using Office.Application.Booking;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Domain.Booking;
using Office.Domain.Tasks;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.115 (the owner, 2026-11-14). (1) Of «تمام، بلّغت المكتب… وحد من المكتب هيرد على حضرتك هنا» at 21:52: «هو حد اصلا
/// من المكتب هيرد عليه دلوقتي؟ المفروض يقول له بكره، صح كده؟ يعني على حسب المواعيد». B-06 follows the office's hours of the
/// panel (booking.office_*): the due time of every office task, and, outside them, a line after a reply that promises a person.
/// (2) Of a client who sent a medical report and was told «معلش لو مش واضح»: once he told his matter, what he needs is asked.
/// </summary>
public sealed class V115OfficeHoursTests
{
    private static readonly MinuteRange TenToEight = new(600, 1200);

    [Theory]
    [InlineData(12, 0, 19, 13, 0)] // while the office answers: within the hour
    [InlineData(18, 59, 19, 19, 59)]
    [InlineData(19, 30, 20, 10, 0)] // its last hour: never «within an hour» past the closing
    [InlineData(21, 52, 20, 10, 0)] // after it: tomorrow at the opening
    [InlineData(8, 0, 19, 10, 0)] // before it: today at the opening
    public void A_task_is_due_by_the_office_s_hours(int hour, int minute, int dueDay, int dueHour, int dueMinute) =>
        Assert.Equal(AvailabilityTests.At(dueDay, dueHour, dueMinute), OfficeHours.DueAt(AvailabilityTests.At(19, hour, minute), TenToEight));

    [Fact]
    public void Without_office_hours_b06_stays_10_to_midnight()
    {
        Assert.Equal(AvailabilityTests.At(19, 23), OfficeHours.DueAt(AvailabilityTests.At(19, 22)));
        MinuteRange? none = null;
        Assert.Equal(AvailabilityTests.At(19, 23), OfficeHours.DueAt(AvailabilityTests.At(19, 22), none));
        Assert.Equal(AvailabilityTests.At(20, 10), OfficeHours.DueAt(AvailabilityTests.At(19, 23, 30), none));
        Assert.Equal(AvailabilityTests.At(19, 10), OfficeHours.DueAt(AvailabilityTests.At(19, 9), none));
    }

    [Fact]
    public void The_next_answer_is_null_while_the_office_answers()
    {
        Assert.Null(OfficeHours.NextAnswer(AvailabilityTests.At(19, 12), TenToEight));
        Assert.Equal(AvailabilityTests.At(20, 10), OfficeHours.NextAnswer(AvailabilityTests.At(19, 21, 52), TenToEight));
    }

    [Theory]
    [InlineData(600, "10 الصبح")]
    [InlineData(1200, "8 بالليل")]
    [InlineData(1440, "12 بالليل")]
    [InlineData(720, "12 الضهر")]
    [InlineData(930, "3:30 العصر")]
    public void The_office_s_hours_are_said_as_people_say_them(int minute, string said) => Assert.Equal(said, BookingWords.Clock(minute));
}

/// <summary>v1.115 from WhatsApp (the flow of <see cref="BookingFlowTests"/>).</summary>
public sealed partial class BookingFlowTests
{
    private const string AnswersLater = "المكتب بيرد من {{opens}} لـ {{closes}}، فحد من المكتب هيرد على حضرتك هنا {{day}} من الساعة {{opens}} 🙏";

    private void OfficeAnswersOn()
    {
        _settings[BookingSettings.OfficeStartKey] = "600";
        _settings[BookingSettings.OfficeEndKey] = "1200";
        _texts[TextKeys.HumanRequest] = new ApprovedText(TextKeys.HumanRequest, 2, "تمام، بلّغت المكتب إن حضرتك عايز تكلم حد، وحد من المكتب هيرد على حضرتك هنا.", 300, true);
        _texts[TextKeys.OfficeAnswersLater] = new ApprovedText(TextKeys.OfficeAnswersLater, 1, AnswersLater, 300, true);
        _script["عايز اكلم حد من المكتب"] = new("human_request", null, "unknown", null, 0.9);
    }

    [Fact]
    public async Task V115_a_person_asked_for_after_the_office_s_hours_hears_when_it_answers()
    {
        OfficeAnswersOn();
        _now = AvailabilityTests.At(19, 21, 52);

        var d = await Say("عايز اكلم حد من المكتب");
        Assert.Equal([TextKeys.HumanRequest, TextKeys.OfficeAnswersLater], Keys(d));
        Assert.Equal("المكتب بيرد من 10 الصبح لـ 8 بالليل، فحد من المكتب هيرد على حضرتك هنا بكرة من الساعة 10 الصبح 🙏", d.Actions[^1].Text);
        Assert.Equal(AvailabilityTests.At(20, 10), Assert.Single(d.Effects!.Tasks).DueAt); // due at the opening, never at night
    }

    [Fact]
    public async Task V115_before_the_office_opens_it_answers_today()
    {
        OfficeAnswersOn();
        _now = AvailabilityTests.At(19, 7, 30);

        var d = await Say("عايز اكلم حد من المكتب");
        Assert.Contains("هنا النهارده من الساعة 10 الصبح", d.Actions[^1].Text);
    }

    [Fact]
    public async Task V115_while_the_office_answers_nothing_is_added()
    {
        OfficeAnswersOn();
        _now = AvailabilityTests.At(19, 12);

        var d = await Say("عايز اكلم حد من المكتب");
        Assert.Equal([TextKeys.HumanRequest], Keys(d));
        Assert.Equal(AvailabilityTests.At(19, 13), Assert.Single(d.Effects!.Tasks).DueAt);
    }

    [Fact]
    public async Task V115_a_promise_whose_text_is_off_never_sends_its_time_alone()
    {
        OfficeAnswersOn();
        _texts.Remove(TextKeys.HumanRequest); // off in «النصوص»: only the active texts are read
        _now = AvailabilityTests.At(19, 21, 52);

        var d = await Say("عايز اكلم حد من المكتب");
        Assert.DoesNotContain(TextKeys.OfficeAnswersLater, Keys(d));
        Assert.Empty(d.Actions);
    }

    [Fact]
    public async Task V115_once_he_told_his_matter_hazem_asks_what_he_needs_then_the_choice()
    {
        _texts[TextKeys.AskNeed] = new ApprovedText(TextKeys.AskNeed, 1, "وصلني اللي بعته 👍 محتاج المكتب يساعد حضرتك في إيه بخصوصه؟", 300, true);
        _texts[TextKeys.BtnTalkToOffice] = new ApprovedText(TextKeys.BtnTalkToOffice, 1, "أكلم حد من المكتب", 20, true);
        _texts[TextKeys.RepeatChoice] = new ApprovedText(TextKeys.RepeatChoice, 1, "T:repeat", 300, true);
        _script["السلام عليكم"] = new("greeting", null, "unknown", null, 0.9);
        _script["دا تقرير الحاله بتاعتي"] = new("tell_problem", null, "unknown", null, 0.6);
        _script["مش عارف اقول ايه تاني"] = new("tell_problem", null, "unknown", null, 0.6);

        await Say("السلام عليكم"); // the welcome and its two buttons
        Assert.Equal([TextKeys.ServicesTell], Keys(await Press("sv:tell")));
        var need = await Say("دا تقرير الحاله بتاعتي");
        Assert.Equal([TextKeys.AskNeed], Keys(need));
        Assert.Equal([ServiceRow.Id(ServiceRow.Menu), ServiceRow.Id(ServiceRow.Human)], Assert.Single(need.Actions).Interactive!.Choices.Select(c => c.Id));
        Assert.Equal([TextKeys.RepeatChoice], Keys(await Say("مش عارف اقول ايه تاني"))); // the same step again: v1.111's choice
    }
}
// «عاوز احكي مشكلتي» with nothing told before still gets «احكيلي الموضوع»: Task16SituationTests (every situation).
