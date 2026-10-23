using Office.Domain.Common;
using Office.Domain.Tasks;

namespace Office.Application.Panel;

/// <summary>Which tasks a list shows (DECISIONS v1.8 T-23: a staff member sees own + unassigned).</summary>
public enum TaskScope
{
    Mine,
    Unassigned,
    All,
}

/// <summary>Active = open, in progress or overdue (by due time); the others by their last change.</summary>
public enum TaskStateFilter
{
    Active,
    Overdue,
    Done,
    Cancelled,
}

/// <summary><paramref name="Search"/>: part of the client's name, or the last digits of the number.</summary>
public sealed record TaskQuery(TaskScope Scope, TaskStateFilter State, int Page, int PageSize = 25, string? Search = null);

public sealed record StaffRef(Guid Id, string Name);

public sealed record TaskSummary(
    Guid Id, string Type, string State, DateTimeOffset DueAt, DateTimeOffset CreatedAt, StaffRef? Staff,
    string? ClientName, string PhoneLast3, string? ClientType, string? MatterType);

public sealed record DashboardTask(
    Guid Id, string Type, string State, DateTimeOffset DueAt, DateTimeOffset CreatedAt, string? ClientName, string? StaffName,
    bool Late = false);

public sealed record Dashboard(
    int Urgent, int Overdue, int Unassigned, int Active, IReadOnlyList<int> UrgentByDay, IReadOnlyList<int> OverdueByDay,
    IReadOnlyList<DashboardTask> Recent, IReadOnlyList<DashboardTask> Priority);

public sealed record TaskList(IReadOnlyList<TaskSummary> Items, int Total, int Page, int PageSize);

/// <summary>What the client said in the turn that opened the task: words only, never a file reference.</summary>
public sealed record ClientWord(string Type, string? Text, string? Caption, string? Transcript, string? MediaStatus, DateTimeOffset? SentAt);

public sealed record TaskRecord(
    Guid Id, string Type, string State, DateTimeOffset DueAt, DateTimeOffset CreatedAt, StaffRef? Staff,
    string? ClientName, string WaId, string? ClientType, string? Source, string? ConversationState,
    string? MatterType, string? MatterSubtype, IReadOnlyList<ClientWord> ClientWords);

public sealed record TaskAbilities(bool Start, bool Done, bool Cancel, bool Assign);

public sealed record TaskView(TaskRecord Task, TaskAbilities Can);

/// <summary>hazem.staff_tasks for the panel (Office.Infrastructure).</summary>
/// <summary>Where a client's file is kept (n8n stored it). Never sent to the browser as such.</summary>
public sealed record ClientFileRef(string Type, string Bucket, string Key, string? MimeType, string? Filename);

/// <summary>A client's file, read from storage.</summary>
public sealed record ClientFile(Stream Content, string ContentType, long? Length, string? Filename);

/// <summary>Storage, read-only (TASK 07c: the api's own key, S3_API_*).</summary>
public interface IClientFiles
{
    bool Configured { get; }

    Task<ClientFile?> OpenAsync(ClientFileRef file, CancellationToken ct);
}

public interface IPanelTaskStore
{
    /// <summary>The file of the n-th message of the task's turn, if it was stored.</summary>
    Task<ClientFileRef?> FileAsync(Guid taskId, int index, CancellationToken ct);

    Task<TaskList> ListAsync(TaskQuery query, Guid? onlyStaff, bool includeUnassigned, CancellationToken ct);

    /// <summary>
    /// The home screen's rows: every unfinished task, and every task opened since <paramref name="since"/>.
    /// <paramref name="restricted"/>: only the tasks of <paramref name="mineOrUnassigned"/> and the
    /// unassigned ones (no staff member = unassigned only).
    /// </summary>
    Task<IReadOnlyList<DashboardTask>> DashboardRowsAsync(Guid? mineOrUnassigned, bool restricted, DateTimeOffset since, CancellationToken ct);

    Task<TaskRecord?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Moves the task only if it is still in <paramref name="from"/>; false when someone changed it first.</summary>
    Task<bool> MoveAsync(Guid id, string from, string to, Guid? assignTo, CancellationToken ct);

    Task<bool> AssignAsync(Guid id, Guid? staffId, CancellationToken ct);

    Task<IReadOnlyList<StaffRef>> ActiveStaffAsync(CancellationToken ct);

    Task AuditAsync(Guid userId, string action, Guid taskId, IReadOnlyDictionary<string, string?> detail, CancellationToken ct);
}

public enum PanelOutcome
{
    Ok,
    NotFound,
    Forbidden,
    Conflict,
    Invalid,
    /// <summary>Needs something only the owner can set up (the mail server, PANEL_SECRET_KEY).</summary>
    Unavailable,
}

/// <summary>A use case's answer: the value, or why not (a stable code, and field errors for forms).</summary>
public sealed record PanelResult<T>(
    PanelOutcome Outcome, T? Value = default, string? Error = null, IReadOnlyDictionary<string, string>? Fields = null)
{
    public static PanelResult<T> Ok(T value) => new(PanelOutcome.Ok, value);
}

/// <summary>
/// The tasks of the panel (S-05). Every action goes through the S-05 machine (StaffTask.Machine),
/// as the staff, whatever the person's role. A staff member never learns that a task they may not
/// see exists (not found, not forbidden).
/// </summary>
public sealed class PanelTasks(IPanelTaskStore store, TimeProvider clock)
{
    public const string ActionStart = "start";
    public const string ActionDone = "done";
    public const string ActionCancel = "cancel";

    public async Task<PanelResult<TaskList>> ListAsync(Viewer viewer, TaskQuery query, CancellationToken ct)
    {
        if (query.Scope == TaskScope.All && !viewer.Can(Permissions.TasksViewAll))
        {
            return new(PanelOutcome.Forbidden);
        }

        var list = query.Scope switch
        {
            TaskScope.Mine => viewer.StaffId is { } me
                ? await store.ListAsync(query, me, includeUnassigned: false, ct)
                : new TaskList([], 0, query.Page, query.PageSize),
            TaskScope.Unassigned => await store.ListAsync(query, null, includeUnassigned: true, ct),
            _ => await store.ListAsync(query, null, includeUnassigned: false, ct),
        };
        return PanelResult<TaskList>.Ok(list);
    }

    public async Task<PanelResult<TaskView>> GetAsync(Viewer viewer, Guid id, CancellationToken ct)
    {
        var task = await store.GetAsync(id, ct);
        return task is null || !MaySee(viewer, task) ? new(PanelOutcome.NotFound) : PanelResult<TaskView>.Ok(View(viewer, task));
    }

    /// <summary>
    /// TASK 07c (the owner's choice): whoever may see the task may open its files. Every opening is
    /// written to the audit (who, which task, which message), never the file's name.
    /// </summary>
    public async Task<PanelResult<ClientFileRef>> FileAsync(Viewer viewer, Guid id, int index, CancellationToken ct)
    {
        var task = await store.GetAsync(id, ct);
        if (task is null || !MaySee(viewer, task) || index < 0 || index >= task.ClientWords.Count)
        {
            return new(PanelOutcome.NotFound);
        }

        if (await store.FileAsync(id, index, ct) is not { } file)
        {
            return new(PanelOutcome.NotFound);
        }

        await store.AuditAsync(viewer.UserId, "task.file_open", id,
            new Dictionary<string, string?> { ["message"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture), ["type"] = file.Type }, ct);
        return PanelResult<ClientFileRef>.Ok(file);
    }

    public async Task<PanelResult<TaskView>> ActAsync(Viewer viewer, Guid id, string action, CancellationToken ct)
    {
        var task = await store.GetAsync(id, ct);
        if (task is null || !MaySee(viewer, task))
        {
            return new(PanelOutcome.NotFound);
        }

        if (!viewer.Can(Permissions.TasksUpdate))
        {
            return new(PanelOutcome.Forbidden);
        }

        var to = action switch
        {
            ActionStart => StaffTaskState.InProgress,
            ActionDone => StaffTaskState.Done,
            ActionCancel => StaffTaskState.Cancelled,
            _ => (StaffTaskState?)null,
        };
        if (to is null)
        {
            return new(PanelOutcome.Invalid);
        }

        var from = StateName.Parse<StaffTaskState>(task.State);
        var check = StaffTask.Machine.Check(from, to.Value, Actor.Staff);
        if (!check.IsOk)
        {
            return new(PanelOutcome.Conflict, Error: check.Error!.Code);
        }

        // Someone who starts or finishes a task nobody holds takes it (07a, the owner's design).
        var assignTo = task.Staff is null && to != StaffTaskState.Cancelled ? viewer.StaffId : null;
        if (!await store.MoveAsync(id, task.State, StateName.Of(to.Value), assignTo, ct))
        {
            return new(PanelOutcome.Conflict, Error: "changed_meanwhile");
        }

        await store.AuditAsync(viewer.UserId, $"task.{action}", id,
            new Dictionary<string, string?> { ["from"] = task.State, ["to"] = StateName.Of(to.Value) }, ct);
        return await GetAsync(viewer, id, ct);
    }

    public async Task<PanelResult<TaskView>> AssignAsync(Viewer viewer, Guid id, Guid? staffId, CancellationToken ct)
    {
        if (!viewer.Can(Permissions.TasksAssign))
        {
            return new(PanelOutcome.Forbidden);
        }

        var task = await store.GetAsync(id, ct);
        if (task is null)
        {
            return new(PanelOutcome.NotFound);
        }

        if (staffId is { } s && (await store.ActiveStaffAsync(ct)).All(x => x.Id != s))
        {
            return new(PanelOutcome.Invalid, Error: "unknown_staff");
        }

        await store.AssignAsync(id, staffId, ct);
        await store.AuditAsync(viewer.UserId, "task.assign", id,
            new Dictionary<string, string?> { ["from"] = task.Staff?.Id.ToString(), ["to"] = staffId?.ToString() }, ct);
        return await GetAsync(viewer, id, ct);
    }

    public Task<IReadOnlyList<StaffRef>> StaffAsync(CancellationToken ct) => store.ActiveStaffAsync(ct);

    private static readonly string[] UrgentTypes = ["urgent_request", "safety_alert"];
    private static readonly string[] Unfinished = ["open", "in_progress", "overdue"];
    public const int DashboardDays = 7;

    /// <summary>
    /// The home screen (TASK 07a): what needs the person now, from the tasks they may see. Late =
    /// unfinished and past its B-06 due time (or already marked overdue). Days are Cairo days.
    /// </summary>
    public async Task<Dashboard> DashboardAsync(Viewer viewer, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cairo = Domain.Tasks.OfficeHours.Cairo;
        var today = TimeZoneInfo.ConvertTime(now, cairo).Date;
        var firstDay = today.AddDays(-(DashboardDays - 1));
        var since = new DateTimeOffset(firstDay, cairo.GetUtcOffset(firstDay));
        var rows = await store.DashboardRowsAsync(viewer.StaffId, !viewer.Can(Permissions.TasksViewAll), since, ct);

        bool Active(DashboardTask t) => Unfinished.Contains(t.State);
        bool Late(DashboardTask t) => Active(t) && (t.State == "overdue" || t.DueAt < now);
        bool Urgent(DashboardTask t) => UrgentTypes.Contains(t.Type);
        int Day(DateTimeOffset at) => (TimeZoneInfo.ConvertTime(at, cairo).Date - firstDay).Days;

        var urgentByDay = new int[DashboardDays];
        var lateByDay = new int[DashboardDays];
        foreach (var t in rows)
        {
            if (Urgent(t) && Day(t.CreatedAt) is var c and >= 0 and < DashboardDays)
            {
                urgentByDay[c]++;
            }

            if (Late(t) && Day(t.DueAt) is var d and >= 0 and < DashboardDays)
            {
                lateByDay[d]++;
            }
        }

        var active = rows.Where(Active).ToList();
        return new Dashboard(
            Urgent: active.Count(Urgent),
            Overdue: active.Count(Late),
            Unassigned: active.Count(t => t.StaffName is null),
            Active: active.Count,
            UrgentByDay: urgentByDay,
            OverdueByDay: lateByDay,
            Recent: rows.OrderByDescending(t => t.CreatedAt).Take(4).Select(t => t with { Late = Late(t) }).ToList(),
            Priority: active.OrderByDescending(Late).ThenByDescending(Urgent).ThenBy(t => t.DueAt).Take(5)
                .Select(t => t with { Late = Late(t) }).ToList());
    }

    /// <summary>T-23: all tasks with view_all; otherwise the person's own and the unassigned ones.</summary>
    public static bool MaySee(Viewer viewer, TaskRecord task) =>
        viewer.Can(Permissions.TasksViewAll)
        || (viewer.Can(Permissions.TasksViewOwn) && (task.Staff is null || task.Staff.Id == viewer.StaffId));

    public static TaskView View(Viewer viewer, TaskRecord task)
    {
        var from = StateName.Parse<StaffTaskState>(task.State);
        bool Allowed(StaffTaskState to) =>
            viewer.Can(Permissions.TasksUpdate) && StaffTask.Machine.Check(from, to, Actor.Staff).IsOk;
        return new TaskView(task, new TaskAbilities(
            Allowed(StaffTaskState.InProgress), Allowed(StaffTaskState.Done), Allowed(StaffTaskState.Cancelled),
            viewer.Can(Permissions.TasksAssign)));
    }
}
