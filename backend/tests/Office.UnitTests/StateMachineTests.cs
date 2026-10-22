using Office.Domain.Booking;
using Office.Domain.Clients;
using Office.Domain.Common;
using Office.Domain.Tasks;
using Office.Domain.Urf;

namespace Office.UnitTests;

/// <summary>
/// S-01..S-06 (DECISIONS v1.5 part 4, unchanged since v1.4). For every machine: every allowed transition passes for
/// each actor allowed to make it and is refused for every other actor; every pair that is not in
/// the table is refused. Then the rules DECISIONS writes as "ممنوع", one test each.
/// </summary>
public sealed class StateMachineTests
{
    public static TheoryData<string> Machines => new() { "S-01", "S-02", "S-03", "S-04", "S-05", "S-06" };

    [Theory]
    [MemberData(nameof(Machines))]
    public void Every_allowed_transition_passes_only_for_its_actors(string machine) => Exhaustive(machine, allowed: true);

    [Theory]
    [MemberData(nameof(Machines))]
    public void Every_other_transition_is_refused_with_a_clear_error(string machine) => Exhaustive(machine, allowed: false);

    private static void Exhaustive(string machine, bool allowed)
    {
        switch (machine)
        {
            case "S-01": Check(Conversation.Machine, allowed); break;
            case "S-02": Check(Intake.Machine, allowed); break;
            case "S-03": Check(Appointment.Machine, allowed); break;
            case "S-04": Check(Payment.Machine, allowed); break;
            case "S-05": Check(StaffTask.Machine, allowed); break;
            default: Check(UrfCase.Machine, allowed); break;
        }
    }

    private static void Check<T>(StateMachine<T> m, bool allowed)
        where T : struct, Enum
    {
        var table = m.Transitions.ToDictionary(t => (t.From, t.To), t => t.By);
        Assert.NotEmpty(table);
        foreach (var from in Enum.GetValues<T>())
        {
            foreach (var to in Enum.GetValues<T>())
            {
                foreach (var actor in Enum.GetValues<Actor>())
                {
                    var r = m.Check(from, to, actor);
                    if (!table.TryGetValue((from, to), out var by))
                    {
                        if (!allowed)
                        {
                            Assert.Equal(DomainError.TransitionNotAllowed, r.Error?.Code);
                            Assert.Contains($"{StateName.Of(from)} -> {StateName.Of(to)}", r.Error!.Message);
                        }
                    }
                    else if (allowed)
                    {
                        Assert.True(by.Contains(actor) ? r.IsOk : r.Error?.Code == DomainError.ActorNotAllowed,
                            $"{m.Name}: {from} -> {to} by {actor}");
                    }
                }
            }
        }
    }

    [Fact]
    public void State_names_are_the_decisions_names()
    {
        Assert.Equal("human_handoff", StateName.Of(ConversationState.HumanHandoff));
        Assert.Equal("cancelled_by_office", StateName.Of(AppointmentState.CancelledByOffice));
        Assert.Equal("duplicate_suspected", StateName.Of(PaymentState.DuplicateSuspected));
        Assert.Equal(IntakeState.HandedToStaff, StateName.Parse<IntakeState>("handed_to_staff"));
    }

    // ---------- S-01 / B-12 ----------

    private static Person Client() => Person.New(WaId.From("201000000041"));

    [Fact]
    public void Staff_message_silences_hazem_and_only_the_manager_unmutes()
    {
        var c = Conversation.Start(Client());
        Assert.True(c.StaffReplied(DateTimeOffset.UnixEpoch).IsOk);
        Assert.False(c.HazemMayAnswer);
        Assert.True(c.Mute(Actor.System).IsOk);
        Assert.Equal(DomainError.ActorNotAllowed, c.Unmute(Actor.Staff).Error?.Code);
        Assert.True(c.Unmute(Actor.Manager).IsOk);
        Assert.True(c.HazemMayAnswer);
    }

    [Fact]
    public void Hazem_comes_back_only_after_12_hours_and_a_new_client_message()
    {
        var c = Conversation.Start(Client());
        var staffAt = DateTimeOffset.Parse("2026-10-22T10:00:00+03:00");
        c.StaffReplied(staffAt);
        c.ClientWroteAt(staffAt.AddHours(11).AddMinutes(59));
        Assert.Equal(ConversationState.HumanHandoff, c.State);
        c.ClientWroteAt(staffAt.AddHours(12));
        Assert.Equal(ConversationState.BotActive, c.State);
    }

    [Fact]
    public void A_group_is_ignored_for_good() =>
        Assert.Equal(DomainError.TransitionNotAllowed, Conversation.StartForGroup(Client()).HandBackToHazem().Error?.Code);

    // ---------- S-02 / B-01 / B-02 ----------

    [Fact]
    public void The_name_is_asked_twice_at_most_and_the_first_request_is_kept()
    {
        var i = Intake.Open(Client());
        Assert.True(i.AskForName().IsOk);
        i.Remember(new PendingRequest("consultation_request", null, "unknown"));
        i.Remember(new PendingRequest("price_question", null, "unknown"));
        Assert.True(i.MayAskNameAgain);
        Assert.True(i.AskForName().IsOk);
        Assert.False(i.MayAskNameAgain);
        Assert.Equal("consultation_request", i.TakePending()?.Intent);
        Assert.True(i.Classify(ClientType.NewClient).IsOk);
        Assert.Equal(IntakeState.Classified, i.State);
    }

    [Fact]
    public void A_closed_intake_cannot_reopen() =>
        Assert.Equal(DomainError.TransitionNotAllowed, Closed().MoveTo(IntakeState.OfferSent).Error?.Code);

    private static Intake Closed()
    {
        var i = Intake.Open(Client());
        i.MoveTo(IntakeState.Closed);
        return i;
    }

    [Theory]
    [InlineData("محمد أحمد علي حسن", 4, true)]
    [InlineData("محمد عبد الرحمن أحمد علي", 4, true)]
    [InlineData("محمد عبد الرحمن أحمد", 3, false)]
    [InlineData("مُحَمَّـد   أحمد", 2, false)]
    [InlineData("Mohamed Ahmed Ali Hassan", 4, true)]
    public void Four_names_make_a_full_name(string text, int parts, bool full)
    {
        Assert.True(PersonName.TryParse(text, out var name));
        Assert.Equal(parts, name.Parts);
        Assert.Equal(full, name.IsFull);
    }

    [Theory]
    [InlineData("عايز احجز استشارة؟")]
    [InlineData("01012345678")]
    [InlineData("محمد")]
    [InlineData("")]
    public void Questions_numbers_and_single_words_are_not_names(string text) => Assert.False(PersonName.TryParse(text, out _));

    [Fact]
    public void Name_is_normalized_without_tashkeel_tatweel_or_extra_spaces()
    {
        PersonName.TryParse("  مُحَمَّـد   أحمد  ", out var name);
        Assert.Equal("مُحَمَّـد   أحمد", name.Raw);
        Assert.Equal("محمد أحمد", name.Normalized);
    }

    [Fact]
    public void A_stored_name_is_restored_as_is_even_if_todays_rules_would_refuse_it()
    {
        var n = PersonName.Restore("محمد؟ عبد الله أحمد علي", "محمد؟ عبد الله أحمد علي");
        Assert.False(PersonName.TryParse(n.Raw, out _));
        Assert.Equal(("محمد؟ عبد الله أحمد علي", 4, true), (n.Raw, n.Parts, n.IsFull));
    }

    [Fact]
    public void Call_phone_is_for_calls_only_and_never_changes_the_wa_id()
    {
        var p = Client();
        Assert.True(p.SetPreferredCallPhone("010 1234 5678").IsOk);
        Assert.Equal("01012345678", p.PreferredCallPhone);
        Assert.Equal("201000000041", p.WaId.Value);
        Assert.False(p.SetPreferredCallPhone("123").IsOk);
    }

    // ---------- S-03 (v1.4) ----------

    private static readonly DateTimeOffset T10 = DateTimeOffset.Parse("2026-10-25T10:00:00+03:00");

    [Fact]
    public void Two_confirmed_appointments_at_the_same_time_are_refused()
    {
        var (first, _) = Appointment.Book("consultation_office", T10, T10.AddMinutes(30), [], []);
        var (second, r) = Appointment.Book("consultation_phone_video", T10.AddMinutes(15), T10.AddMinutes(30), [first!], []);
        Assert.Null(second);
        Assert.Equal("slot_taken", r.Error?.Code);
    }

    [Fact]
    public void An_appointment_over_a_calendar_event_is_refused() =>
        Assert.Equal("calendar_busy",
            Appointment.Book("consultation_office", T10, T10.AddMinutes(30), [], [new BusyInterval(T10.AddMinutes(20), T10.AddHours(2))]).Result.Error?.Code);

    [Fact]
    public void A_cancelled_appointment_frees_its_time()
    {
        var (first, _) = Appointment.Book("consultation_office", T10, T10.AddMinutes(30), [], []);
        first!.CancelByClient();
        Assert.True(Appointment.Book("consultation_office", T10, T10.AddMinutes(30), [first], []).Result.IsOk);
    }

    [Fact]
    public void Reschedule_is_the_same_booking_confirmed_again_at_the_new_time()
    {
        var (a, _) = Appointment.Book("consultation_office", T10, T10.AddMinutes(30), [], []);
        Assert.True(a!.Reschedule(T10.AddHours(2), T10.AddHours(2.5), [a], []).IsOk);
        Assert.Equal(AppointmentState.Confirmed, a.State);
        Assert.Equal(1, a.Reschedules);
        Assert.Equal(T10.AddHours(2), a.Start);
    }

    [Fact]
    public void The_client_cannot_mark_a_no_show_or_complete() =>
        Assert.Equal(DomainError.ActorNotAllowed, Appointment.Book("x", T10, T10.AddMinutes(15), [], []).Appointment!.Complete(Actor.System).Error?.Code);

    // ---------- S-04 (v1.4) ----------

    [Fact]
    public void Only_the_staff_member_marks_refunded()
    {
        var p = Payment.Request(Money.Of(500));
        p.ReceiptArrived();
        p.Verify(Actor.Lawyer);
        p.RequestRefund();
        Assert.Equal(DomainError.ActorNotAllowed, p.MarkRefunded(Actor.System).Error?.Code);
        Assert.Equal(DomainError.ActorNotAllowed, p.MarkRefunded(Actor.Lawyer).Error?.Code);
        Assert.True(p.MarkRefunded(Actor.Staff).IsOk);
    }

    [Fact]
    public void The_system_never_verifies_a_transfer()
    {
        var p = Payment.Request(Money.Of(500));
        p.ReceiptArrived();
        Assert.Equal(DomainError.ActorNotAllowed, p.Verify(Actor.System).Error?.Code);
        Assert.Equal(PaymentState.ProofReceived, p.State);
    }

    [Fact]
    public void Partial_payment_is_not_settled_until_the_rest_arrives_and_is_verified()
    {
        var p = Payment.Request(Money.Of(1000));
        p.ReceiptArrived();
        Assert.True(p.MarkPartial(Actor.Lawyer, Money.Of(600)).IsOk);
        Assert.False(p.IsSettled);
        Assert.Equal(400, p.Missing.Pounds);
        p.ReceiptArrived();
        p.Verify(Actor.Lawyer);
        Assert.True(p.IsSettled);
    }

    [Fact]
    public void A_duplicate_receipt_is_an_alert_only()
    {
        var p = Payment.Request(Money.Of(500));
        p.ReceiptArrived();
        p.FlagDuplicate();
        Assert.True(p.Verify(Actor.Lawyer).IsOk);
    }

    [Fact]
    public void Partial_must_be_less_than_due()
    {
        var p = Payment.Request(Money.Of(500));
        p.ReceiptArrived();
        Assert.Equal("not_partial", p.MarkPartial(Actor.Lawyer, Money.Of(500)).Error?.Code);
    }

    // ---------- S-05 ----------

    [Fact]
    public void A_task_past_its_due_time_becomes_overdue_and_staff_can_still_finish_it()
    {
        var t = StaffTask.Open(StaffTaskType.HumanRequested, T10);
        t.CheckOverdue(T10.AddMinutes(-1));
        Assert.Equal(StaffTaskState.Open, t.State);
        t.CheckOverdue(T10.AddMinutes(1));
        Assert.Equal(StaffTaskState.Overdue, t.State);
        Assert.True(t.Finish(Actor.Staff).IsOk);
        Assert.Equal(DomainError.TransitionNotAllowed, t.Cancel(Actor.Staff).Error?.Code);
    }

    // ---------- S-06 / B-11 tests table ----------

    private const string Policy = "urf-2026-10-22";

    [Fact]
    public void Age_17_closes_the_request_with_its_policy_version()
    {
        var u = UrfCase.Start(UrfType.NewContract);
        Assert.True(u.CheckAges(25, 17, Policy).IsOk);
        Assert.Equal(UrfCaseState.RejectedAge, u.State);
        Assert.Equal(Policy, u.PolicyVersion);
    }

    [Theory]
    [InlineData(true, true, true, Guardian.Father)]
    [InlineData(false, true, true, Guardian.Brother)]
    [InlineData(false, false, true, Guardian.Uncle)]
    public void Guardian_order_is_father_then_brother_then_uncle(bool father, bool brother, bool uncle, Guardian expected) =>
        Assert.Equal(expected, UrfCase.GuardianFrom(father, brother, uncle));

    [Fact]
    public void No_guardian_is_refused_as_office_policy()
    {
        var u = UrfCase.Start(UrfType.NewContract);
        u.CheckGuardian(false, false, false, Policy);
        Assert.Equal(UrfCaseState.RejectedGuardian, u.State);
    }

    [Fact]
    public void Proof_goes_to_a_consultation_and_unclear_asks_first()
    {
        Assert.Equal(UrfCaseState.RedirectedConsultation, UrfCase.Start(UrfType.Proof).State);
        var u = UrfCase.Unclear();
        Assert.True(u.SetType(UrfType.Divorce).IsOk);
        Assert.Equal(UrfCaseState.Collecting, u.State);
    }

    [Fact]
    public void Only_the_lawyer_approves_or_rejects_after_booking()
    {
        var u = UrfCase.Start(UrfType.NewContract);
        u.AskForDocuments();
        u.Booked();
        Assert.Equal(DomainError.ActorNotAllowed, UrfCase.Machine.Check(u.State, UrfCaseState.Approved, Actor.Staff).Error?.Code);
        Assert.True(u.Reject(Policy).IsOk);
        Assert.Equal(UrfCaseState.LawyerRejected, u.State);
    }
}
