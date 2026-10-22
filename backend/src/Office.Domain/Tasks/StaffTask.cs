using Office.Domain.Common;

namespace Office.Domain.Tasks;

public enum StaffTaskState
{
    Open,
    InProgress,
    Done,
    Overdue,
    Cancelled,
}

/// <summary>S-05 task types (v1.4).</summary>
public enum StaffTaskType
{
    ExistingClientMessage,
    LateReschedule,
    Refund,
    PriceQuote,
    DiscountRequest,
    Complaint,
    HumanRequested,
    SafetyAlert,
}

/// <summary>S-05. Code and tests only in TASK 04; the staff, the panel and the table come in TASK 05/07.</summary>
public sealed class StaffTask
{
    private static readonly StaffTaskState[] Unfinished = [StaffTaskState.Open, StaffTaskState.InProgress, StaffTaskState.Overdue];

    public static readonly StateMachine<StaffTaskState> Machine = new("S-05 staff task",
    [
        new(StaffTaskState.Open, StaffTaskState.InProgress, Actor.Staff),
        new(StaffTaskState.Open, StaffTaskState.Done, Actor.Staff),
        new(StaffTaskState.InProgress, StaffTaskState.Done, Actor.Staff),
        new(StaffTaskState.Open, StaffTaskState.Overdue, Actor.System),
        new(StaffTaskState.InProgress, StaffTaskState.Overdue, Actor.System),
        new(StaffTaskState.Overdue, StaffTaskState.InProgress, Actor.Staff),
        new(StaffTaskState.Overdue, StaffTaskState.Done, Actor.Staff),
        .. Unfinished.Select(s => new Transition<StaffTaskState>(s, StaffTaskState.Cancelled, Actor.Staff, Actor.System)),
    ]);

    private StaffTask(Guid id, StaffTaskType type, DateTimeOffset dueAt) => (Id, Type, DueAt) = (id, type, dueAt);

    public Guid Id { get; }

    public StaffTaskType Type { get; }

    /// <summary>Computed from B-06 when the task opens.</summary>
    public DateTimeOffset DueAt { get; }

    public StaffTaskState State { get; private set; } = StaffTaskState.Open;

    public static StaffTask Open(StaffTaskType type, DateTimeOffset dueAt) => new(Guid.NewGuid(), type, dueAt);

    public Result Start(Actor by) => Move(StaffTaskState.InProgress, by);

    public Result Finish(Actor by) => Move(StaffTaskState.Done, by);

    public Result Cancel(Actor by) => Move(StaffTaskState.Cancelled, by);

    /// <summary>due_at passed: overdue (then an alert to the lawyer and an apology to the client).</summary>
    public Result CheckOverdue(DateTimeOffset now) =>
        now > DueAt && State is StaffTaskState.Open or StaffTaskState.InProgress ? Move(StaffTaskState.Overdue, Actor.System) : Result.Ok;

    private Result Move(StaffTaskState to, Actor by)
    {
        var r = Machine.Check(State, to, by);
        if (r.IsOk)
        {
            State = to;
        }

        return r;
    }
}

