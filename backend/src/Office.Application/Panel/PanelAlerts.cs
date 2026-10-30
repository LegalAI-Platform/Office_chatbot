namespace Office.Application.Panel;

// TASK 07c (DECISIONS v1.11): the office's alerts. A task that went overdue (S-05), an urgent one
// (B-09) or a client asking for a person (B-12) makes one alert. The panel's bell shows it to whoever
// may see that task; an e-mail from the no-reply sender tells them once. No client's words, name or
// number ever goes into an e-mail: the kind of task, its due time and a link to the panel.

public static class AlertKinds
{
    public const string Overdue = "task_overdue";
    public const string Urgent = "task_urgent";
    public const string Human = "task_human";

    /// <summary>TASK 13 (v1.18): a nightly job failed, or n8n workflows failed in an hour. No task; the managers only.</summary>
    public const string System = "system";
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

/// <summary>An alert as the bell shows it. A system alert has no task (<see cref="SystemWhat"/> instead).</summary>
public sealed record PanelAlert(long Id, string Kind, Guid? TaskId, string? TaskType, string? ClientName, DateTimeOffset? DueAt,
    DateTimeOffset CreatedAt, bool Read, string? SystemWhat = null);

/// <summary>An alert whose e-mail has not gone out yet.</summary>
public sealed record AlertToMail(long Id, string Kind, Guid? TaskId, string? TaskType, DateTimeOffset? DueAt, Guid? StaffId, string? SystemWhat = null);

public interface IPanelAlertStore
{
    /// <summary>Alerts for tasks made since alerts.since that need one and have none; how many were made.</summary>
    Task<int> MakeAsync(CancellationToken ct);

    /// <summary>TASK 13: an alert for every failed nightly job, and one for every hour n8n workflows failed in.</summary>
    Task<int> MakeSystemAsync(CancellationToken ct);

    /// <summary>The e-mail of every active manager (a system alert is theirs only).</summary>
    Task<IReadOnlyList<string>> ManagersAsync(CancellationToken ct);

    /// <summary>Not e-mailed yet, from the last day only (an old backlog is never mailed in a burst).</summary>
    Task<IReadOnlyList<AlertToMail>> UnsentAsync(CancellationToken ct);

    Task MarkEmailedAsync(long id, CancellationToken ct);

    /// <summary>Alerts whose task was finished before the e-mail: marked as handled, never mailed.</summary>
    Task<int> DropFinishedAsync(CancellationToken ct);

    /// <summary>The e-mail of every active account that may see a task with this staff member (null: unassigned).</summary>
    Task<IReadOnlyList<string>> RecipientsAsync(Guid? staffId, CancellationToken ct);

    Task<IReadOnlyList<PanelAlert>> ListAsync(Viewer viewer, int limit, CancellationToken ct);

    Task<int> UnreadAsync(Viewer viewer, CancellationToken ct);

    /// <summary>Marks these (or, with null, every alert the viewer may see) read.</summary>
    Task MarkReadAsync(Viewer viewer, IReadOnlyList<long>? ids, CancellationToken ct);
}

public sealed class PanelAlerts(IPanelAlertStore store, IPanelMailer mailer)
{
    public const int Shown = 30;

    /// <summary>
    /// Makes the new alerts and mails the unsent ones. Returns (made, mailed, failed) and the kind of
    /// the first failure that the mailer threw (its type only: an address may be in its message).
    /// </summary>
    public async Task<(int Made, int Mailed, int Failed, string? Error)> RunAsync(string panelUrl, CancellationToken ct)
    {
        var made = await store.MakeAsync(ct) + await store.MakeSystemAsync(ct);
        int mailed = 0, failed = 0;
        string? error = null;
        if (!mailer.Configured)
        {
            return (made, 0, 0, null);
        }

        await store.DropFinishedAsync(ct);
        foreach (var alert in await store.UnsentAsync(ct))
        {
            var system = alert.Kind == AlertKinds.System;
            foreach (var to in system ? await store.ManagersAsync(ct) : await store.RecipientsAsync(alert.StaffId, ct))
            {
                try
                {
                    if (await mailer.SendAsync(system ? PanelMails.SystemAlert(to, alert.SystemWhat, panelUrl) : PanelMails.Alert(to, alert, panelUrl), ct))
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

    public async Task<(IReadOnlyList<PanelAlert> Items, int Unread)> ListAsync(Viewer viewer, CancellationToken ct) =>
        (await store.ListAsync(viewer, Shown, ct), await store.UnreadAsync(viewer, ct));

    public Task MarkReadAsync(Viewer viewer, IReadOnlyList<long>? ids, CancellationToken ct) => store.MarkReadAsync(viewer, ids, ct);
}
