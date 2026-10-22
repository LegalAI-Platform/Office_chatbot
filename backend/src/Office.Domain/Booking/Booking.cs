using Office.Domain.Common;

namespace Office.Domain.Booking;

// S-03 and S-04 as DECISIONS v1.4 has them: booking is confirmed at once (no hold, no staff),
// the receipt is stored but never reviewed by the system, and only the lawyer marks a transfer.
// Code and tests only in TASK 04; their tables come with the booking tasks (08/09).

public enum AppointmentState
{
    Confirmed,
    Rescheduled,
    Completed,
    NoShow,
    CancelledByClient,
    CancelledByOffice,
}

/// <summary>A time the lawyer's calendar shows as busy (any event closes its time, B-24).</summary>
public sealed record BusyInterval(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => start < End && Start < end;
}

public sealed class Appointment
{
    public static readonly StateMachine<AppointmentState> Machine = new("S-03 appointment",
    [
        new(AppointmentState.Confirmed, AppointmentState.Completed, Actor.Lawyer, Actor.Staff),
        new(AppointmentState.Confirmed, AppointmentState.Rescheduled, Actor.System),
        new(AppointmentState.Rescheduled, AppointmentState.Confirmed, Actor.System),
        new(AppointmentState.Confirmed, AppointmentState.NoShow, Actor.Lawyer, Actor.Staff),
        new(AppointmentState.Confirmed, AppointmentState.CancelledByClient, Actor.System),
        new(AppointmentState.Confirmed, AppointmentState.CancelledByOffice, Actor.Lawyer, Actor.System),
    ]);

    private Appointment(Guid id, string service, DateTimeOffset start, DateTimeOffset end)
    {
        Id = id;
        Service = service;
        Start = start;
        End = end;
    }

    public Guid Id { get; }

    public string Service { get; }

    public DateTimeOffset Start { get; private set; }

    public DateTimeOffset End { get; private set; }

    public AppointmentState State { get; private set; } = AppointmentState.Confirmed;

    /// <summary>B-15: one free change only.</summary>
    public int Reschedules { get; private set; }

    /// <summary>
    /// B-08.3: the client picked a free slot; it is confirmed at once. Refused when it overlaps
    /// another confirmed appointment or any calendar event (S-03 "ممنوع").
    /// </summary>
    public static (Appointment? Appointment, Result Result) Book(string service, DateTimeOffset start, DateTimeOffset end,
        IEnumerable<Appointment> lawyerAppointments, IEnumerable<BusyInterval> calendar)
    {
        var free = SlotIsFree(start, end, lawyerAppointments, calendar);
        return free.IsOk ? (new Appointment(Guid.NewGuid(), service, start, end), Result.Ok) : (null, free);
    }

    public static Result SlotIsFree(DateTimeOffset start, DateTimeOffset end,
        IEnumerable<Appointment> lawyerAppointments, IEnumerable<BusyInterval> calendar, Guid? except = null)
    {
        if (end <= start)
        {
            return Result.Fail("invalid_slot", "an appointment ends after it starts");
        }

        if (lawyerAppointments.Any(a => a.Id != except && a.State == AppointmentState.Confirmed && a.Start < end && start < a.End))
        {
            return Result.Fail("slot_taken", "two confirmed appointments at the same time");
        }

        return calendar.Any(b => b.Overlaps(start, end))
            ? Result.Fail("calendar_busy", "the lawyer's calendar has an event at that time")
            : Result.Ok;
    }

    /// <summary>B-15: rescheduled → confirmed again at the new time, same booking.</summary>
    public Result Reschedule(DateTimeOffset start, DateTimeOffset end,
        IEnumerable<Appointment> lawyerAppointments, IEnumerable<BusyInterval> calendar)
    {
        var r = Machine.Check(State, AppointmentState.Rescheduled, Actor.System);
        if (r.IsOk)
        {
            r = SlotIsFree(start, end, lawyerAppointments, calendar, except: Id);
        }

        if (!r.IsOk)
        {
            return r;
        }

        (Start, End) = (start, end);
        Reschedules++;
        State = AppointmentState.Confirmed; // rescheduled is momentary: it goes back to confirmed
        return Result.Ok;
    }

    public Result Complete(Actor by) => Move(AppointmentState.Completed, by);

    public Result MarkNoShow(Actor by) => Move(AppointmentState.NoShow, by);

    public Result CancelByClient() => Move(AppointmentState.CancelledByClient, Actor.System);

    public Result CancelByOffice(Actor by) => Move(AppointmentState.CancelledByOffice, by);

    private Result Move(AppointmentState to, Actor by)
    {
        var r = Machine.Check(State, to, by);
        if (r.IsOk)
        {
            State = to;
        }

        return r;
    }
}

public enum PaymentState
{
    Requested,
    ProofReceived,
    DuplicateSuspected,
    Verified,
    Partial,
    NotReceived,
    RefundRequested,
    Refunded,
}

/// <summary>S-04 (v1.4). The payment state never changes the appointment by itself.</summary>
public sealed class Payment
{
    // From the S-04 diagram. Two arrows are read from B-05 (and written in the TASK 04 report):
    // after a receipt the lawyer may still find nothing (proof_received → not_received), and a
    // duplicate is an alert only, so the lawyer still marks it (duplicate_suspected → his marks).
    public static readonly StateMachine<PaymentState> Machine = new("S-04 payment",
    [
        new(PaymentState.Requested, PaymentState.ProofReceived, Actor.System),
        new(PaymentState.Requested, PaymentState.NotReceived, Actor.Lawyer),
        new(PaymentState.ProofReceived, PaymentState.Verified, Actor.Lawyer),
        new(PaymentState.ProofReceived, PaymentState.Partial, Actor.Lawyer),
        new(PaymentState.ProofReceived, PaymentState.NotReceived, Actor.Lawyer),
        new(PaymentState.ProofReceived, PaymentState.DuplicateSuspected, Actor.System),
        new(PaymentState.DuplicateSuspected, PaymentState.Verified, Actor.Lawyer),
        new(PaymentState.DuplicateSuspected, PaymentState.Partial, Actor.Lawyer),
        new(PaymentState.DuplicateSuspected, PaymentState.NotReceived, Actor.Lawyer),
        new(PaymentState.Partial, PaymentState.ProofReceived, Actor.System),
        new(PaymentState.Verified, PaymentState.RefundRequested, Actor.System),
        new(PaymentState.Partial, PaymentState.RefundRequested, Actor.System),
        new(PaymentState.RefundRequested, PaymentState.Refunded, Actor.Staff), // the staff member ONLY (B-22)
    ]);

    private Payment(Guid id, Money due) => (Id, Due) = (id, due);

    public Guid Id { get; }

    public Money Due { get; }

    public Money Received { get; private set; } = Money.Zero;

    public PaymentState State { get; private set; } = PaymentState.Requested;

    /// <summary>Rule 5 (B-05/B-22): a service is confirmed only once the full amount arrived.</summary>
    public bool IsSettled => State == PaymentState.Verified && Received >= Due;

    public Money Missing => Received >= Due ? Money.Zero : Due - Received;

    /// <summary>The booking was confirmed and the payment details were sent (B-08.4).</summary>
    public static Payment Request(Money due) => new(Guid.NewGuid(), due);

    /// <summary>B-05: the receipt image arrived. Stored only, never reviewed by the system.</summary>
    public Result ReceiptArrived() => Move(PaymentState.ProofReceived, Actor.System);

    /// <summary>B-05: the same image on another booking: an alert for the lawyer, nothing stops.</summary>
    public Result FlagDuplicate() => Move(PaymentState.DuplicateSuspected, Actor.System);

    public Result Verify(Actor by)
    {
        var r = Move(PaymentState.Verified, by);
        if (r.IsOk)
        {
            Received = Due;
        }

        return r;
    }

    public Result MarkPartial(Actor by, Money received)
    {
        if (received >= Due)
        {
            return Result.Fail("not_partial", "a partial amount is less than the amount due");
        }

        var r = Move(PaymentState.Partial, by);
        if (r.IsOk)
        {
            Received = received;
        }

        return r;
    }

    public Result MarkNotReceived(Actor by) => Move(PaymentState.NotReceived, by);

    public Result RequestRefund() => Move(PaymentState.RefundRequested, Actor.System);

    /// <summary>Only the staff member who really transferred the money (B-22).</summary>
    public Result MarkRefunded(Actor by) => Move(PaymentState.Refunded, by);

    private Result Move(PaymentState to, Actor by)
    {
        var r = Machine.Check(State, to, by);
        if (r.IsOk)
        {
            State = to;
        }

        return r;
    }
}
