using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Office.Application.Panel;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Panel;

/// <summary>
/// hazem.panel_alerts and panel_alert_reads (migration 0012). Who may see an alert is who may see
/// its task (T-23): the manager and the lawyer every one, a staff member their own and the unassigned.
/// </summary>
public sealed class PanelAlertStore(HazemDbContext db) : IPanelAlertStore
{
    private static readonly string[] Urgent = ["urgent_request", "safety_alert"];

    public Task<int> MakeAsync(CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO hazem.panel_alerts (kind, task_id)
        SELECT k.kind, t.id
          FROM hazem.staff_tasks t
          CROSS JOIN LATERAL (VALUES
            (CASE WHEN t.state = 'overdue' THEN {AlertKinds.Overdue} END),
            (CASE WHEN t.type = ANY({Urgent}) AND t.state NOT IN ('done', 'cancelled') THEN {AlertKinds.Urgent} END),
            (CASE WHEN t.type = 'human_requested' AND t.state NOT IN ('done', 'cancelled') THEN {AlertKinds.Human} END)) AS k(kind)
         WHERE k.kind IS NOT NULL
           AND t.created_at >= (SELECT value::timestamptz FROM hazem.settings WHERE key = 'alerts.since')
        ON CONFLICT (kind, task_id) DO NOTHING
        """, ct);

    public async Task<IReadOnlyList<AlertToMail>> UnsentAsync(CancellationToken ct) =>
        await (from a in db.PanelAlerts.AsNoTracking()
               join t in db.StaffTasks.AsNoTracking() on a.TaskId equals t.Id
               where a.EmailedAt == null && a.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1)
                     && (a.Kind == AlertKinds.Overdue ? t.State == "overdue" : t.State != "done" && t.State != "cancelled")
               orderby a.Id
               select new AlertToMail(a.Id, a.Kind, a.TaskId, t.Type, t.DueAt, t.StaffId)).ToListAsync(ct);

    /// <summary>An alert whose task was finished before its e-mail went out is never mailed.</summary>
    public Task<int> DropFinishedAsync(CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE hazem.panel_alerts a SET emailed_at = now()
          FROM hazem.staff_tasks t
         WHERE t.id = a.task_id AND a.emailed_at IS NULL
           AND (t.state IN ('done', 'cancelled') OR (a.kind = {AlertKinds.Overdue} AND t.state <> 'overdue'))
        """, ct);

    public Task MarkEmailedAsync(long id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.panel_alerts SET emailed_at = now() WHERE id = {id}", ct);

    public async Task<IReadOnlyList<string>> RecipientsAsync(Guid? staffId, CancellationToken ct) =>
        await db.PanelUsers.AsNoTracking()
            .Where(u => u.Active && u.Approval == PanelUser.ApprovalApproved
                        && (u.Role == Permissions.Manager || u.Role == Permissions.Lawyer
                            || (u.Role == Permissions.Staff && (staffId == null || u.StaffId == staffId))))
            .Select(u => u.Email).ToListAsync(ct);

    public async Task<IReadOnlyList<PanelAlert>> ListAsync(Viewer viewer, int limit, CancellationToken ct)
    {
        var rows = await (from a in Visible(viewer)
                          join t in db.StaffTasks.AsNoTracking() on a.TaskId equals t.Id
                          join p in db.Persons.AsNoTracking() on t.PersonId equals p.Id
                          orderby a.CreatedAt descending, a.Id descending
                          select new
                          {
                              a.Id, a.Kind, a.TaskId, t.Type, p.NameRaw, t.DueAt, a.CreatedAt,
                              Read = db.PanelAlertReads.Any(r => r.AlertId == a.Id && r.UserId == viewer.UserId),
                          }).Take(limit).ToListAsync(ct);
        return rows.Select(r => new PanelAlert(r.Id, r.Kind, r.TaskId, r.Type, r.NameRaw, r.DueAt, r.CreatedAt, r.Read)).ToList();
    }

    public Task<int> UnreadAsync(Viewer viewer, CancellationToken ct) =>
        Visible(viewer).CountAsync(a => !db.PanelAlertReads.Any(r => r.AlertId == a.Id && r.UserId == viewer.UserId), ct);

    public async Task MarkReadAsync(Viewer viewer, IReadOnlyList<long>? ids, CancellationToken ct)
    {
        var visible = Visible(viewer);
        if (ids is not null)
        {
            visible = visible.Where(a => ids.Contains(a.Id));
        }

        var unread = await visible.Where(a => !db.PanelAlertReads.Any(r => r.AlertId == a.Id && r.UserId == viewer.UserId))
            .Select(a => a.Id).ToArrayAsync(ct);
        if (unread.Length > 0)
        {
            // One statement, however many there are.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.panel_alert_reads (alert_id, user_id)
                SELECT unnest({unread}), {viewer.UserId} ON CONFLICT DO NOTHING
                """, ct);
        }
    }

    private IQueryable<PanelAlertRow> Visible(Viewer viewer)
    {
        var alerts = db.PanelAlerts.AsNoTracking();
        if (viewer.Can(Permissions.TasksViewAll))
        {
            return alerts;
        }

        var staff = viewer.StaffId;
        return from a in alerts
               join t in db.StaffTasks.AsNoTracking() on a.TaskId equals t.Id
               where t.StaffId == null || (staff != null && t.StaffId == staff)
               select a;
    }
}

/// <summary>
/// Every minute: tasks past their due time become overdue (S-05), new alerts are made and mailed.
/// Off in tests (Alerts:RunWorker=false): they call the steps themselves.
/// </summary>
public sealed class AlertWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<AlertWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Alerts:RunWorker", true))
        {
            return;
        }

        var panelUrl = config["Panel:PublicUrl"] is { Length: > 0 } u ? u : "http://127.0.0.1:8080";
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var overdue = await scope.ServiceProvider.GetRequiredService<Lists.TaskMaintenance>().MarkOverdueAsync(stop);
                var (made, mailed, failed, error) = await scope.ServiceProvider.GetRequiredService<PanelAlerts>().RunAsync(panelUrl, stop);
                if (overdue + made + mailed > 0)
                {
                    log.LogInformation("Alerts: {Overdue} task(s) overdue, {Made} alert(s), {Mailed} e-mail(s)", overdue, made, mailed);
                }

                if (failed > 0)
                {
                    log.LogWarning("Alerts: {Failed} e-mail(s) not sent ({Error})", failed, error ?? "see the mailer's warning");
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("Alerts round failed: {Kind}: {Message}", e.GetType().Name, e.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
