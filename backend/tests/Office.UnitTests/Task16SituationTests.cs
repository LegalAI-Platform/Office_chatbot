using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Understanding;
using Office.Domain.Clients;
using Office.Domain.Common;

namespace Office.UnitTests;

/// <summary>
/// TASK 16 (DECISIONS v1.39, B-28): Hazem answers the client's situation, not just his message. Every situation (nothing
/// open, a time held for the transfer, a paid urgent call waiting, a confirmed appointment tomorrow, a written service waiting
/// for its papers) × every kind of message (a greeting, «عايز أحجز», a price, «عاوز احكي مشكلتي», an unclear message,
/// «فاكرني؟»), each with its right reply and the forbidden rules checked; then the rules one by one, and the name.
/// </summary>
public sealed partial class BookingFlowTests
{
    /// <summary>The paid offers and payment requests the forbidden rule 1 keeps from a client with a consultation open.</summary>
    private static readonly string[] PaidOffers =
        [TextKeys.ConsultationOffer, TextKeys.CaseOffer, TextKeys.UrgentPrice, TextKeys.UrgentPay, TextKeys.BookingHold];

    private void ApproveTask16Texts()
    {
        foreach (var (k, v) in new Dictionary<string, string>
                 {
                     [TextKeys.SituationUrgent] = "أكيد{{call}} 👋 عاجل مدفوع، المستشار هيكلم حضرتك {{when}}.",
                     [TextKeys.SituationHeld] = "أهلًا{{call}} 👋 ({{service}}) {{day}} {{time}} ممسوك لحد الساعة {{deadline}}.",
                     [TextKeys.SituationConfirmed] = "أهلًا{{call}} 👋 ({{service}}) {{day}} الساعة {{time}} متأكد ✅.",
                     [TextKeys.SituationOrder] = "أهلًا{{call}} 👋 طلب حضرتك ({{service}}) {{status}}.",
                     [TextKeys.SituationTopic] = "تمام{{call}}، وصلت ✅ وهتبقى قدام المستشار.",
                     [TextKeys.RepeatChoice] = "معلش لو مش واضح، تحب تشوف خدمات المكتب، ولا تكلم حد من المكتب؟",
                     [TextKeys.BtnTalkToOffice] = "أكلم حد من المكتب",
                     [TextKeys.Greeting2] = "اتفضل{{call}}، أقدر أساعد حضرتك في إيه؟",
                     [TextKeys.Greeting3] = "أنا معاك{{call}}، قولّي محتاج إيه.",
                     [TextKeys.TopicReceived2] = "وصلت يا فندم ✅",
                     [TextKeys.TopicReceived3] = "تمام{{call}}، وصلت ✅",
                     [TextKeys.AboutHazem] = "أنا حازم، المساعد الرقمي",
                     [TextKeys.Unclear] = "معلش مش واضحلي المطلوب.",
                     [TextKeys.TopicReceived] = "تمام، وصلت ✅",
                     [TextKeys.WelcomeBack] = "أهلًا بحضرتك تاني يا {{first_name}} 👋",
                     [TextKeys.WelcomeChoose] = "حضرتك تحب تعرف خدمات المكتب، ولا تحكيلي محتاج إيه؟",
                 })
        {
            _texts[k] = new ApprovedText(k, 1, v, k.StartsWith("btn_", StringComparison.Ordinal) ? 20 : 300, true);
        }

        _script["السلام عليكم"] = new("greeting", null, "unknown", null, 0.95);
        _script["عايز أحجز"] = new("consultation_request", null, "unknown", null, 0.95);
        _script["الاستشارة بكام"] = new("price_question", "consultation_phone_video", "unknown", null, 0.95);
        _script["عاوز احكي مشكلتي"] = new("tell_problem", null, "unknown", null, 0.95);
        _script["ألو"] = new("other", null, "unknown", null, 0.95);
        _script["انت فاكرني؟"] = new("my_status", null, "unknown", null, 0.95);
        _script["خدمات"] = new("show_services", null, "unknown", null, 0.95);
        _script["اي خدمات المكتب"] = new("show_services", null, "unknown", null, 0.95);
        _script["مستعجل جدا"] = new("urgent", null, "unknown", null, 0.95);
        _script["جاري بني على أرضي"] = new("describe_matter", null, "unknown", null, 0.95, MatterType: "real_estate");
    }

    /// <summary>A new client who greeted once (the welcome), then the situation of the cell.</summary>
    private async Task<Guid> InSituation(string situation)
    {
        ApproveTask16Texts();
        await Say("السلام عليكم");
        var person = _rows[Number].Person.Id;
        var tomorrow = Now.AddDays(1);
        switch (situation)
        {
            case "held":
                _booking.Appointments.Add(new AppointmentInfo(Guid.NewGuid(), person, BookingService.PhoneVideo.Code, Now.AddHours(3),
                    Now.AddHours(3.25), AppointmentInfo.PendingPayment, 0, 500, null, Now.AddHours(1)));
                break;
            case "urgent":
                _booking.Appointments.Add(new AppointmentInfo(Guid.NewGuid(), person, BookingService.Urgent.Code, Now.AddMinutes(5),
                    Now.AddMinutes(20), AppointmentInfo.Confirmed, 0, 1000, null));
                break;
            case "confirmed":
                _booking.Appointments.Add(new AppointmentInfo(Guid.NewGuid(), person, BookingService.Office.Code, tomorrow, tomorrow.AddHours(1),
                    AppointmentInfo.Confirmed, 0, 1000, null));
                break;
            case "order_priced":
                _booking.Orders.Add(new MemoryBooking.Order(person, "document_review") { State = "awaiting_price" });
                break;
            case "order":
                _booking.Orders.Add(new MemoryBooking.Order(person, "document_review"));
                break;
        }

        return person;
    }

    public static TheoryData<string, string, string> Cells() => new()
    {
        // nothing open: as before
        { "new", "السلام عليكم", TextKeys.Greeting },
        { "new", "عايز أحجز", TextKeys.ConsultationOffer },
        { "new", "الاستشارة بكام", TextKeys.ConsultationOffer },
        { "new", "عاوز احكي مشكلتي", TextKeys.ServicesTell },
        { "new", "ألو", TextKeys.Unclear },
        { "new", "انت فاكرني؟", TextKeys.AboutHazem },
        // a time held for the transfer
        { "held", "السلام عليكم", TextKeys.SituationHeld },
        { "held", "عايز أحجز", TextKeys.SituationHeld },
        { "held", "الاستشارة بكام", TextKeys.SituationHeld },
        { "held", "عاوز احكي مشكلتي", TextKeys.ServicesTell },
        { "held", "ألو", TextKeys.SituationHeld },
        { "held", "انت فاكرني؟", TextKeys.SituationHeld },
        // a paid urgent call waiting
        { "urgent", "السلام عليكم", TextKeys.SituationUrgent },
        { "urgent", "عايز أحجز", TextKeys.SituationUrgent },
        { "urgent", "الاستشارة بكام", TextKeys.SituationUrgent },
        { "urgent", "عاوز احكي مشكلتي", TextKeys.ServicesTell },
        { "urgent", "ألو", TextKeys.SituationUrgent },
        { "urgent", "انت فاكرني؟", TextKeys.SituationUrgent },
        // a confirmed appointment tomorrow
        { "confirmed", "السلام عليكم", TextKeys.SituationConfirmed },
        { "confirmed", "عايز أحجز", TextKeys.SituationConfirmed },
        { "confirmed", "الاستشارة بكام", TextKeys.SituationConfirmed },
        { "confirmed", "عاوز احكي مشكلتي", TextKeys.ServicesTell },
        { "confirmed", "ألو", TextKeys.SituationConfirmed },
        { "confirmed", "انت فاكرني؟", TextKeys.SituationConfirmed },
        // a written service waiting for its papers: no consultation open, so a price still gets the offer
        { "order", "السلام عليكم", TextKeys.Greeting }, // v1.43: waiting for his papers is no answer to a greeting
        { "order", "عايز أحجز", TextKeys.ConsultationOffer },
        { "order", "الاستشارة بكام", TextKeys.ConsultationOffer },
        { "order", "عاوز احكي مشكلتي", TextKeys.ServicesTell },
        { "order", "ألو", TextKeys.Unclear },
        { "order", "انت فاكرني؟", TextKeys.SituationOrder }, // he asked
        // v1.43: a written order the office works on (its price being set): his situation first, as before
        { "order_priced", "السلام عليكم", TextKeys.SituationOrder },
        { "order_priced", "ألو", TextKeys.SituationOrder },
        { "order_priced", "انت فاكرني؟", TextKeys.SituationOrder },
    };

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task V139_every_situation_and_every_kind_of_message_gets_its_reply(string situation, string message, string expected)
    {
        await InSituation(situation);
        var keys = Keys(await Say(message));
        Assert.Contains(expected, keys);
        if (situation is "held" or "urgent" or "confirmed")
        {
            Assert.DoesNotContain(keys, PaidOffers.Contains); // the forbidden rule 1
        }
    }

    [Fact]
    public async Task V139_the_situation_reply_says_what_he_has_with_its_day_and_time()
    {
        await InSituation("confirmed");
        Assert.Equal("أهلًا 👋 (مقابلة في المكتب) بكرة الساعة 12:00 م متأكد ✅.", (await Say("السلام عليكم")).Actions[^1].Text);
    }

    [Fact]
    public async Task V139_a_paid_urgent_client_is_told_the_lawyer_calls_within_minutes_and_his_urgent_words_make_no_second_task()
    {
        await InSituation("urgent");
        Assert.Equal("أكيد 👋 عاجل مدفوع، المستشار هيكلم حضرتك خلال دقايق.", (await Say("السلام عليكم")).Actions[^1].Text);
        var urgent = await Say("مستعجل جدا");
        Assert.Contains(TextKeys.SituationUrgent, Keys(urgent));
        Assert.DoesNotContain(TextKeys.UrgentPrice, Keys(urgent)); // never a second paid call
        Assert.DoesNotContain(PendingTask.UrgentRequest, TaskKinds(urgent)); // v1.40: the task of his receipt is the one (one bell, one e-mail)
    }

    [Fact]
    public async Task V139_the_topic_told_for_an_open_item_reaches_the_office()
    {
        await InSituation("urgent");
        Assert.Equal([TextKeys.ServicesTell], Keys(await Say("عاوز احكي مشكلتي")));
        var story = await Say("جاري بني على أرضي");
        Assert.Equal([TextKeys.SituationTopic], Keys(story));
        Assert.Contains(PendingTask.ExistingClientMessage, TaskKinds(story));
    }

    [Fact]
    public async Task V139_forbidden_rule_2_the_same_answer_twice_in_a_row_becomes_the_services_or_a_person()
    {
        await InSituation("new");
        Assert.Contains(TextKeys.ServicesMenu, Keys(await Say("خدمات")));
        var again = await Say("اي خدمات المكتب");
        Assert.Equal([TextKeys.RepeatChoice], Keys(again));
        var buttons = again.Actions[^1].Interactive!.Choices.Select(c => c.Id).ToList();
        Assert.Equal([ServiceRow.Id(ServiceRow.Menu), ServiceRow.Id(ServiceRow.Human)], buttons);

        // «أكلم حد من المكتب» is «عايز أكلم حد»
        var person = await Send(Pressed(ServiceRow.Id(ServiceRow.Human)));
        Assert.Contains(PendingTask.HumanRequested, TaskKinds(person));
    }

    [Fact]
    public async Task V139_a_client_asking_his_situation_twice_is_told_it_twice()
    {
        await InSituation("urgent");
        Assert.Contains(TextKeys.SituationUrgent, Keys(await Say("السلام عليكم")));
        Assert.Equal([TextKeys.SituationUrgent], Keys(await Say("انت فاكرني؟"))); // he asked for it: no rule 2
        Assert.Equal([TextKeys.RepeatChoice], Keys(await Say("ألو"))); // the same answer to an unclear message: rule 2
    }

    [Fact]
    public async Task V139_the_greeting_s_wordings_go_in_turn_and_never_twice_running()
    {
        await InSituation("new");
        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            seen.Add(Keys(await Say("السلام عليكم")).Single(k => k.StartsWith("greeting", StringComparison.Ordinal)));
        }

        Assert.Equal([TextKeys.Greeting, TextKeys.Greeting2, TextKeys.Greeting3, TextKeys.Greeting], seen);
    }

    [Theory]
    [InlineData("محمد أحمد علي حسن", "male", " يا أستاذ محمد")]
    [InlineData("منى سعيد", "female", " يا أستاذة منى")]
    [InlineData("ابو عماد العنتيل", "male", " يا ابو عماد")]
    [InlineData("نور الدين", "unknown", "")]
    public void V139_hazem_calls_the_client_by_the_name_he_typed_only_when_its_gender_is_clear(string name, string gender, string call)
    {
        var p = Person.New(WaId.From(Number));
        Assert.True(PersonName.TryParse(name, out var n, minWords: 1));
        p.GiveName(n, gender);
        Assert.Equal(call, Salutation.Call(p));
        Assert.Equal("", Salutation.Call(Person.New(WaId.From(Number)))); // no name: none
    }

    [Fact]
    public async Task V139_the_name_s_gender_the_ai_read_is_kept_and_used_in_the_situation_reply()
    {
        await InSituation("confirmed");
        _script["منى سعيد"] = new("provide_name", null, "unknown", "منى سعيد", 0.95, NameGender: "female");
        await Say("منى سعيد");
        Assert.Equal(Person.Female, _rows[Number].Person.NameGender);
        Assert.StartsWith("أهلًا يا أستاذة منى 👋", (await Say("السلام عليكم")).Actions[^1].Text);
    }

    [Fact]
    public async Task V139_the_abu_emad_conversation_replayed_gets_his_situation_the_services_and_never_a_paid_offer()
    {
        await InSituation("urgent");
        var all = new List<string>();
        async Task<List<string>> Step(string text)
        {
            var keys = Keys(await Say(text));
            all.AddRange(keys);
            return keys;
        }

        Assert.Contains(TextKeys.SituationUrgent, await Step("السلام عليكم"));
        Assert.Contains(TextKeys.SituationUrgent, await Step("انت فاكرني؟"));
        Assert.Contains(TextKeys.ServicesMenu, await Step("خدمات"));
        Assert.Equal([TextKeys.RepeatChoice], await Step("اي خدمات المكتب")); // the same list again: never word for word
        Assert.Equal([TextKeys.ServicesTell], await Step("عاوز احكي مشكلتي"));
        Assert.Equal([TextKeys.SituationTopic], await Step("جاري بني على أرضي"));
        Assert.DoesNotContain(all, PaidOffers.Contains);
    }

    /// <summary>v1.39: the situation as RatingStore.SituationAsync reads it, from the in-memory bookings and orders.</summary>
    private sealed class MemorySituation(MemoryBooking booking, Dictionary<string, List<string>> lastReplies) : IIntakeHistory
    {
        public Task<IntakeFacts> FactsAsync(Guid personId, string waId, Guid intakeId, CancellationToken ct) =>
            Task.FromResult(new IntakeFacts(null, [])); // never a returning client here

        public Task<SituationFacts> SituationAsync(Guid personId, string waId, DateTimeOffset now, CancellationToken ct)
        {
            var a = booking.Appointments.Where(x => x.PersonId == personId && x.IsOpen && x.End > now)
                .OrderBy(x => x.State == AppointmentInfo.Confirmed && x.Service == BookingService.Urgent.Code ? 0
                    : x.State == AppointmentInfo.PendingPayment ? 1 : 2)
                .ThenBy(x => x.Start)
                .Select(x => new OpenAppointment(x.Service, x.State, x.Start, x.HoldUntil)).FirstOrDefault();
            var o = booking.Orders.LastOrDefault(x => x.Person == personId && x.State is "collecting" or "awaiting_price" or "awaiting_payment" or "in_progress");
            return Task.FromResult(new SituationFacts(a, o is null ? null : new OpenOrder(o.Kind, o.State),
                lastReplies.TryGetValue(waId, out var k) ? k : []));
        }
    }
}
