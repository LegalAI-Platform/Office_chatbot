using System.Collections.Immutable;

namespace Office.Application.Panel;

// TASK 07c (DECISIONS v1.11): the office's alerts. A task that went overdue (S-05), an urgent one
// (B-09) or a client asking for a person (B-12) makes one alert. The panel's bell shows it to whoever
// may see that task; an e-mail from the no-reply sender tells them once. No client's words, name or
// number ever goes into an e-mail: the kind of task, its due time and a link to the panel.
// v1.105: every other new task makes a «task_new» alert too (the bell and Windows, no e-mail).
// v1.106: the bell's stream also names the areas of the office's data that changed (DataSignal), so every page on the
// screen asks again at once.

public static class AlertKinds
{
    public const string Overdue = "task_overdue";
    public const string Urgent = "task_urgent";
    public const string Human = "task_human";

    /// <summary>v1.31 (B-26): a client rated a service «يحتاج للتحسين» (rating_low). The managers only.</summary>
    public const string Rating = "task_rating";

    /// <summary>TASK 13 (v1.18): a nightly job failed, or n8n workflows failed in an hour. No task; the managers only.</summary>
    public const string System = "system";

    /// <summary>
    /// v1.105 (migration 0076): any other task Hazem opened for the office, whatever its type: a client's answer needs
    /// someone (the owner: «نشوف حل عام»). Whoever may see the task hears at once, in the bell and Windows; no e-mail.
    /// </summary>
    public const string New = "task_new";

    /// <summary>
    /// v1.108 (migration 0077, the owner: «الموظف»): an urgent call nobody took in time. Its own task reached the staff,
    /// instead of a new «urgent_call_unanswered» task. Like an urgent request: Windows keeps it on the screen, with a sound.
    /// </summary>
    public const string Escalated = "task_escalated";

    /// <summary>v1.108 (the owner: «الإيميل للعاجل والمتأخر بس»): the kinds that are also e-mailed. The others are the bell's only.</summary>
    public static readonly IReadOnlyList<string> Mailed = [Urgent, Escalated, Overdue, System];
}

/// <summary>
/// v1.108 (the owner: «التنبيه يروح للي عليه الدور بس»): whose job an alert is. It is not a permission: the maintenance
/// account may open everything, yet the office's daily alerts are not its job.
/// <list type="bullet">
/// <item>A task someone holds: that person only.</item>
/// <item><see cref="Lawyer"/>: an urgent call (until it is escalated), pricing a service, an Urfi case's review.</item>
/// <item><see cref="Office"/>: the staff. Everything else Hazem opens, and an urgent call nobody took in time.</item>
/// <item><see cref="Managers"/>: the system's problems and a low rating (the manager and the maintenance account).</item>
/// </list>
/// </summary>
public static class AlertAudience
{
    public const string Lawyer = "lawyer";
    public const string Office = "office";
    public const string Managers = "managers";

    /// <summary>The task types that are the lawyer's while nobody holds them and they are not escalated.</summary>
    public static readonly IReadOnlyList<string> LawyerWork =
        ["urgent_request", "safety_alert", "urgent_call_failed", "urgent_call_unanswered", "service_pricing", "urf_review"];

    public const string RatingType = "rating_low";

    /// <summary>The audience a signed-in role belongs to.</summary>
    public static string Of(string role) => role switch
    {
        Permissions.Lawyer => Lawyer,
        Permissions.Staff => Office,
        _ => Managers, // the manager and the maintenance account
    };

    /// <summary>The roles whose accounts get an audience's e-mails.</summary>
    public static IReadOnlyList<string> Roles(string audience) => audience switch
    {
        Lawyer => [Permissions.Lawyer],
        Office => [Permissions.Staff],
        _ => [Permissions.Manager, Permissions.Maintenance],
    };

    /// <summary>Whose job an unheld alert is (a held task's alerts are its holder's, but a rating's and the system's).</summary>
    public static string For(string kind, string? taskType, bool escalated) =>
        kind == AlertKinds.System || kind == AlertKinds.Rating || taskType == RatingType ? Managers
        : kind == AlertKinds.Escalated || escalated ? Office
        : taskType is not null && LawyerWork.Contains(taskType) ? Lawyer
        : Office;
}

/// <summary>What a system alert is about (hazem.panel_alerts.system_what).</summary>
public static class SystemWhat
{
    public const string Backup = "backup";
    public const string Healthcheck = "healthcheck";
    public const string Drill = "drill";
    public const string LogScan = "log_scan";
    public const string N8n = "n8n";
    /// <summary>A client's turn the API failed on too many times (migration 0020): set aside, the client unblocked.</summary>
    public const string Turn = "turn";
    /// <summary>0032: three messages or more in an hour that the AI could not read at all (its key, an error, a timeout).</summary>
    public const string Ai = "ai";
}

/// <summary>
/// An alert as the bell shows it. A system alert has no task (<see cref="SystemWhat"/> instead). v1.104, for the Windows
/// notification: the client's number's last 3 digits (never the number), the case's matter, and who has the task (null:
/// nobody yet). Never what the client wrote (the owner: «من غير كلامه»).
/// </summary>
public sealed record PanelAlert(long Id, string Kind, Guid? TaskId, string? TaskType, string? ClientName, DateTimeOffset? DueAt,
    DateTimeOffset CreatedAt, bool Read, string? SystemWhat = null, string? PhoneLast3 = null, string? MatterType = null,
    string? StaffName = null);

/// <summary>An alert whose e-mail has not gone out yet. v1.108: <paramref name="Escalated"/>, its urgent task reached the staff.</summary>
public sealed record AlertToMail(long Id, string Kind, Guid? TaskId, string? TaskType, DateTimeOffset? DueAt, Guid? StaffId, string? SystemWhat = null,
    bool Escalated = false);

public interface IPanelAlertStore
{
    /// <summary>Alerts for tasks made since alerts.since that need one and have none; how many were made.</summary>
    Task<int> MakeAsync(CancellationToken ct);

    /// <summary>TASK 13: an alert for every failed nightly job, and one for every hour n8n workflows failed in.</summary>
    Task<int> MakeSystemAsync(CancellationToken ct);

    /// <summary>
    /// Not e-mailed yet, from the last day only (an old backlog is never mailed in a burst). v1.108: the kinds in
    /// <see cref="AlertKinds.Mailed"/> only.
    /// </summary>
    Task<IReadOnlyList<AlertToMail>> UnsentAsync(CancellationToken ct);

    Task MarkEmailedAsync(long id, CancellationToken ct);

    /// <summary>Alerts whose task was finished before the e-mail: marked as handled, never mailed.</summary>
    Task<int> DropFinishedAsync(CancellationToken ct);

    /// <summary>
    /// v1.108: the e-mail of every active account whose job the alert is: the task's holder, else its
    /// <see cref="AlertAudience"/>.
    /// </summary>
    Task<IReadOnlyList<string>> RecipientsAsync(AlertToMail alert, CancellationToken ct);

    Task<IReadOnlyList<PanelAlert>> ListAsync(Viewer viewer, int limit, CancellationToken ct);

    Task<int> UnreadAsync(Viewer viewer, CancellationToken ct);

    /// <summary>v1.104: the unread system alerts among them (the bell puts the clients' requests first).</summary>
    Task<int> UnreadSystemAsync(Viewer viewer, CancellationToken ct);

    /// <summary>
    /// v1.106: a stamp for each area of the office's data the panel shows (the tasks, the appointments, the payments…): it
    /// changes whenever a row of the area is added, changed or removed. Counts only, never client data.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> DataStampsAsync(CancellationToken ct);

    /// <summary>Marks these (or, with null, every alert the viewer may see) read.</summary>
    Task MarkReadAsync(Viewer viewer, IReadOnlyList<long>? ids, CancellationToken ct);
}

/// <summary>
/// v1.104: wakes the open panels' streams at once (the owner: real time). It says only that something changed; each panel
/// then asks for what it may see. v1.106: two of them on the same stream, the bell's (<see cref="AlertSignal"/>) and the
/// home's (<see cref="DataSignal"/>).
/// </summary>
public abstract class PanelSignal
{
    private long _version;
    private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many pulses so far: a stream that was busy writing still sees one it missed.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Completes at the next <see cref="Pulse"/>.</summary>
    public Task Next => Volatile.Read(ref _next).Task;

    public void Pulse()
    {
        Interlocked.Increment(ref _version);
        Interlocked.Exchange(ref _next, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }
}

/// <summary>v1.104: alerts were made or read, so the bell and Windows' notification hear at once.</summary>
public sealed class AlertSignal : PanelSignal
{
}

/// <summary>
/// v1.106 (the owner: «الداش بورد كلها ريل تايم ؟» … «عايزين كله يشتغل بنفس الطريقه دي»): an area of the office's data
/// the panel shows changed (the tasks, the appointments, the payments, the clients…), so the pages on the screen that
/// show it ask again at once instead of at their minute. The stream names the areas, never a row.
/// </summary>
public sealed class DataSignal : PanelSignal
{
    private IReadOnlyDictionary<string, string>? _stamps;
    private ImmutableDictionary<string, long> _changedAt = ImmutableDictionary.Create<string, long>(StringComparer.Ordinal);

    /// <summary>
    /// Wakes the streams for the areas whose stamp differs from the last look; the first look only notes them. Returns the
    /// areas it woke them for (none: nothing changed). Only the worker looks, one round at a time.
    /// </summary>
    public IReadOnlyList<string> Observe(IReadOnlyDictionary<string, string> stamps)
    {
        var last = Interlocked.Exchange(ref _stamps, stamps);
        if (last is null)
        {
            return [];
        }

        string[] changed = [.. stamps.Where(s => !last.TryGetValue(s.Key, out var was) || was != s.Value).Select(s => s.Key).Order(StringComparer.Ordinal)];
        if (changed.Length == 0)
        {
            return [];
        }

        // noted before the pulse, so a stream that wakes always finds its areas
        var at = Version + 1;
        ImmutableInterlocked.Update(ref _changedAt, d => d.SetItems(changed.Select(a => KeyValuePair.Create(a, at))));
        Pulse();
        return changed;
    }

    /// <summary>The areas that changed after this version: a stream that missed a wake still hears every one.</summary>
    public IReadOnlyList<string> ChangedSince(long version) =>
        [.. Volatile.Read(ref _changedAt).Where(a => a.Value > version).Select(a => a.Key).Order(StringComparer.Ordinal)];
}

public sealed class PanelAlerts(IPanelAlertStore store, IPanelMailer mailer, AlertSignal signal, DataSignal data)
{
    public const int Shown = 30;

    /// <summary>
    /// Makes the new alerts and mails the unsent ones. Returns (made, mailed, failed) and the kind of
    /// the first failure that the mailer threw (its type only: an address may be in its message).
    /// v1.104: <paramref name="system"/> false skips the system checks (the worker runs them once a minute, the clients'
    /// requests every few seconds).
    /// </summary>
    public async Task<(int Made, int Mailed, int Failed, string? Error)> RunAsync(string panelUrl, CancellationToken ct, bool system = true)
    {
        var made = await store.MakeAsync(ct) + (system ? await store.MakeSystemAsync(ct) : 0);
        if (made > 0)
        {
            signal.Pulse();
        }

        int mailed = 0, failed = 0;
        string? error = null;
        if (!mailer.Configured)
        {
            return (made, 0, 0, null);
        }

        await store.DropFinishedAsync(ct);
        foreach (var alert in await store.UnsentAsync(ct))
        {
            var isSystem = alert.Kind == AlertKinds.System;
            // v1.108: only whose job it is (the holder, else its audience), never everyone who may see the task
            foreach (var to in await store.RecipientsAsync(alert, ct))
            {
                try
                {
                    if (await mailer.SendAsync(isSystem ? PanelMails.SystemAlert(to, alert.SystemWhat, panelUrl) : PanelMails.Alert(to, alert, panelUrl), ct))
                    {
                        mailed++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    // Counted as not sent: marked below anyway, so nobody gets it twice.
                    failed++;
                    error ??= e.GetType().Name + (e.InnerException is { } inner ? " <- " + inner.GetType().Name : "");
                }
            }

            // Marked even when one address failed: the bell still shows it, and a retry every
            // minute would mail everyone else again.
            await store.MarkEmailedAsync(alert.Id, ct);
        }

        return (made, mailed, failed, error);
    }

    public async Task<(IReadOnlyList<PanelAlert> Items, int Unread, int UnreadSystem)> ListAsync(Viewer viewer, CancellationToken ct) =>
        (await store.ListAsync(viewer, Shown, ct), await store.UnreadAsync(viewer, ct), await store.UnreadSystemAsync(viewer, ct));

    /// <summary>Read on one device: the person's other tabs and devices hear it at once too (v1.104).</summary>
    public async Task MarkReadAsync(Viewer viewer, IReadOnlyList<long>? ids, CancellationToken ct)
    {
        await store.MarkReadAsync(viewer, ids, ct);
        signal.Pulse();
    }

    /// <summary>
    /// v1.106: the worker looks every round which areas of the office's data changed, whoever changed them (the panel,
    /// Hazem, n8n or a job), and wakes the same stream with their names. The first look only notes where things are.
    /// Returns the areas it woke them for.
    /// </summary>
    public async Task<IReadOnlyList<string>> WatchDataAsync(CancellationToken ct) => data.Observe(await store.DataStampsAsync(ct));
}
