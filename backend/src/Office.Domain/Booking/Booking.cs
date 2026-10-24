using Office.Domain.Common;

namespace Office.Domain.Booking;

// S-03 and S-04 (DECISIONS v1.13): a picked time is held until the receipt image (pending_payment), the
// receipt confirms it, and a hold that runs out frees its time; the receipt is stored but never reviewed
// by the system, and only a person of the office marks a transfer or a refund. The database (0013, 0014)
// keeps the same rules for the real bookings; these classes are the rules in code, with their tests.

public enum AppointmentState
{
    PendingPayment, // 1.13: held for the client until the receipt image
    Expired,        // 1.13: the hold ran out, the time is free again
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
        new(AppointmentState.PendingPayment, AppointmentState.Confirmed, Actor.System),
        new(AppointmentState.PendingPayment, AppointmentState.Expired, Actor.System),
        new(AppointmentState.PendingPayment, AppointmentState.CancelledByClient, Actor.System),
        new(AppointmentState.PendingPayment, AppointmentState.CancelledByOffice, Actor.Lawyer, Actor.System),
        new(AppointmentState.Expired, AppointmentState.Confirmed, Actor.System), // B-05 (1.13): the receipt came late and the time is still free
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

        if (lawyerAppointments.Any(a => a.Id != except && a.State is (AppointmentState.Confirmed or AppointmentState.PendingPayment) && a.Start < end && start < a.End))
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

    /// <summary>B-08.3 (1.13): the picked time, held until the receipt image (the database keeps it free of others).</summary>
    public static (Appointment? Appointment, Result Result) Hold(string service, DateTimeOffset start, DateTimeOffset end,
        IEnumerable<Appointment> lawyerAppointments, IEnumerable<BusyInterval> calendar)
    {
        var (a, r) = Book(service, start, end, lawyerAppointments, calendar);
        if (a is not null)
        {
            a.State = AppointmentState.PendingPayment;
        }

        return (a, r);
    }

    /// <summary>B-08.5 (1.13): the receipt image arrived; never a confirmation of the payment itself.</summary>
    public Result ReceiptArrived() => Move(AppointmentState.Confirmed, Actor.System);

    /// <summary>B-08.6 (1.13): the hold's time ran out without a receipt.</summary>
    public Result Expire() => Move(AppointmentState.Expired, Actor.System);

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
    // TASK 09 (v1.13, written in its report): every office person reviews (P1) and records a refund (P2),
    // never the system; a new receipt after «ما وصلش» is a receipt again; the three marks correct each
    // other; a refund may be due before the review (the client cancels within 24 hours of his receipt).
    private static readonly Actor[] People = [Actor.Lawyer, Actor.Manager, Actor.Staff];

    public static readonly StateMachine<PaymentState> Machine = new("S-04 payment",
    [
        new(PaymentState.Requested, PaymentState.ProofReceived, Actor.System),
        new(PaymentState.Requested, PaymentState.NotReceived, People),
        new(PaymentState.ProofReceived, PaymentState.Verified, People),
        new(PaymentState.ProofReceived, PaymentState.Partial, People),
        new(PaymentState.ProofReceived, PaymentState.NotReceived, People),
        new(PaymentState.ProofReceived, PaymentState.DuplicateSuspected, Actor.System),
        new(PaymentState.DuplicateSuspected, PaymentState.Verified, People),
        new(PaymentState.DuplicateSuspected, PaymentState.Partial, People),
        new(PaymentState.DuplicateSuspected, PaymentState.NotReceived, People),
        new(PaymentState.Partial, PaymentState.ProofReceived, Actor.System),
        new(PaymentState.NotReceived, PaymentState.ProofReceived, Actor.System),
        new(PaymentState.Verified, PaymentState.Partial, People),
        new(PaymentState.Verified, PaymentState.NotReceived, People),
        new(PaymentState.Partial, PaymentState.Verified, People),
        new(PaymentState.Partial, PaymentState.NotReceived, People),
        new(PaymentState.NotReceived, PaymentState.Verified, People),
        new(PaymentState.NotReceived, PaymentState.Partial, People),
        new(PaymentState.Verified, PaymentState.RefundRequested, Actor.System),
        new(PaymentState.Partial, PaymentState.RefundRequested, Actor.System),
        new(PaymentState.ProofReceived, PaymentState.RefundRequested, Actor.System),
        new(PaymentState.DuplicateSuspected, PaymentState.RefundRequested, Actor.System),
        new(PaymentState.RefundRequested, PaymentState.Refunded, People), // a person who transferred it, never the system (B-22, P2)
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
