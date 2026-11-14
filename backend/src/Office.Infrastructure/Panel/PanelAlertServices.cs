using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Office.Application.Conversations;
using Office.Application.Panel;
using Office.Infrastructure.Monitoring;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Panel;

/// <summary>
/// hazem.panel_alerts and panel_alert_reads (migration 0012). Who may see an alert is who may see
/// its task (T-23): the manager and the lawyer every one, a staff member their own and the unassigned.
/// TASK 13 (0019): a system alert (a failed nightly job, n8n failures in an hour) has no task: the managers only.
/// </summary>
public sealed class PanelAlertStore(HazemDbContext db) : IPanelAlertStore
{
    // 1.33 (B-09). v1.110: a paid urgent call's one task, at each of its steps (PendingTask.UrgentCallSteps).
    private static readonly string[] Urgent = ["urgent_request", "safety_alert", "urgent_call", "urgent_call_failed", "urgent_call_unanswered"];

    private static readonly string[] UrgentCall = [.. PendingTask.UrgentCallSteps];

    /// <summary>
    /// v1.110 (the owner, of the bell's red count: «أيوه، ضيفها»): a task that was finished (done, cancelled) leaves its alerts
    /// read for everyone: out of the red count and the tab's title, dimmed in the list, whoever finished it.
    /// </summary>
    private static readonly string[] Finished = ["done", "cancelled"];

    /// <summary>The types whose own alert already says a new task came (urgent, a client asking for someone, a low rating).</summary>
    private static readonly string[] OwnAlert = [.. Urgent, "human_requested", "rating_low"];

    /// <summary>v1.111: a receipt the office has not reviewed yet (as «الإيصالات» lists them).</summary>
    private static readonly string[] Waiting = [Office.Application.Booking.PaymentInfo.ProofReceived, Office.Application.Booking.PaymentInfo.DuplicateSuspected];

    public async Task<int> MakeAsync(CancellationToken ct) => await MakeForTasksAsync(ct) + await MakeForReceiptsAsync(ct);

    // v1.105: every other new task makes a «task_new» alert, whatever its type (a general rule, not a list). Only a task
    // of the last 10 minutes: a deploy never floods the bell with the old ones. v1.112: or one a client's new message
    // joined in the last 10 minutes (its old alert was taken away with it: told again as new, at the top)
    private Task<int> MakeForTasksAsync(CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO hazem.panel_alerts (kind, task_id)
        SELECT k.kind, t.id
          FROM hazem.staff_tasks t
          CROSS JOIN LATERAL (VALUES
            (CASE WHEN t.state = 'overdue' THEN {AlertKinds.Overdue} END),
            (CASE WHEN t.type = ANY({Urgent}) AND t.state NOT IN ('done', 'cancelled') THEN {AlertKinds.Urgent} END),
            (CASE WHEN t.type = 'human_requested' AND t.state NOT IN ('done', 'cancelled') THEN {AlertKinds.Human} END),
            (CASE WHEN t.type = 'rating_low' AND t.state NOT IN ('done', 'cancelled') THEN {AlertKinds.Rating} END),
            (CASE WHEN t.type <> ALL({OwnAlert}) AND t.state NOT IN ('done', 'cancelled')
                       AND COALESCE(t.joined_at, t.created_at) >= now() - interval '10 minutes'
                  THEN {AlertKinds.New} END)) AS k(kind)
         WHERE k.kind IS NOT NULL
           AND t.created_at >= (SELECT value::timestamptz FROM hazem.settings WHERE key = 'alerts.since')
        ON CONFLICT (kind, task_id) DO NOTHING
        """, ct);

    /// <summary>
    /// v1.111 (the owner: «لو عميل بعت صوره الايصال وعاوز تأكيد الدفع»): a payment waiting for its review gets one alert, from
    /// its first receipt since it was last reviewed (more photos of it, one alert); a receipt of the last 10 minutes only, as a
    /// new task's (a deploy never floods the bell). The urgent call's receipt has its own urgent alert (B-09).
    /// </summary>
    private Task<int> MakeForReceiptsAsync(CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO hazem.panel_alerts (kind, receipt_id)
        SELECT DISTINCT ON (p.id) {AlertKinds.Receipt}, r.id
          FROM hazem.payment_receipts r
          JOIN hazem.payments p ON p.id = r.payment_id
          LEFT JOIN hazem.appointments a ON a.id = p.appointment_id
         WHERE p.state = ANY({Waiting})
           AND a.service IS DISTINCT FROM {Office.Application.Booking.BookingService.Urgent.Code}
           AND r.received_at >= now() - interval '10 minutes'
           AND r.received_at >= (SELECT value::timestamptz FROM hazem.settings WHERE key = 'alerts.since')
           AND r.received_at >= COALESCE(p.reviewed_at, '-infinity'::timestamptz)
           AND NOT EXISTS (SELECT 1 FROM hazem.panel_alerts x JOIN hazem.payment_receipts y ON y.id = x.receipt_id
                            WHERE y.payment_id = p.id AND y.received_at >= COALESCE(p.reviewed_at, '-infinity'::timestamptz))
         ORDER BY p.id, r.received_at
        ON CONFLICT DO NOTHING
        """, ct);

    public async Task<int> MakeSystemAsync(CancellationToken ct)
    {
        // Each insert says how many alerts it made (ON CONFLICT: one per failed run, one per hour of n8n failures).
        const string Since = "GREATEST(now() - interval '2 days', (SELECT value::timestamptz FROM hazem.settings WHERE key = 'alerts.since'))";
        var failed = await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO hazem.panel_alerts (kind, system_what, system_ref)
            SELECT 'system', r.kind, 'run:' || r.id FROM hazem.ops_runs r
             WHERE r.ok = false AND r.finished_at >= {Since}
            ON CONFLICT (system_ref) DO NOTHING
            """, ct);
        var n8n = await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO hazem.panel_alerts (kind, system_what, system_ref)
            SELECT 'system', 'n8n', 'n8n:' || to_char(h.hour AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24')
              FROM hazem.n8n_error_hours({Since}) h
            ON CONFLICT (system_ref) DO NOTHING
            """, ct);
        // A client's turn set aside after too many failed tries (migration 0020): one alert each, no number, no text.
        var turns = await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO hazem.panel_alerts (kind, system_what, system_ref)
            SELECT 'system', 'turn', 'turn:' || f.turn_id FROM hazem.failed_turns({Since}) f
            ON CONFLICT (system_ref) DO NOTHING
            """, ct);
        // 0032: the AI that reads the clients' messages is down (its key refused, out of credit, an error, a timeout): three
        // messages or more in one hour it could not read at all, one alert that hour. Counts only.
        var ai = await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO hazem.panel_alerts (kind, system_what, system_ref)
            SELECT 'system', 'ai', 'ai:' || to_char(h.hour AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24')
              FROM (SELECT date_trunc('hour', c.created_at) AS hour FROM hazem.classifications c
                     WHERE c.created_at >= {Since}
                       AND c.fallback_reason ~ '^(backstop:)?(no_key|timeout|error|invalid_output)$'
                     GROUP BY 1 HAVING count(*) >= 3) h
            ON CONFLICT (system_ref) DO NOTHING
            """, ct);
        // A night with no backup at all (the PC or Docker off at 03:00, the scheduled task gone, a run killed half way):
        // once a backup has ever run, 26 hours with none that finished is an alert, one a day (a failed one has its own).
        var missed = await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO hazem.panel_alerts (kind, system_what, system_ref)
            SELECT 'system', 'backup', 'missed:' || to_char(now() AT TIME ZONE 'Africa/Cairo', 'YYYY-MM-DD')
             WHERE EXISTS (SELECT 1 FROM hazem.ops_runs WHERE kind = 'backup')
               AND NOT EXISTS (SELECT 1 FROM hazem.ops_runs WHERE kind = 'backup' AND finished_at IS NOT NULL AND started_at > now() - interval '26 hours')
            ON CONFLICT (system_ref) DO NOTHING
            """, ct);
        return failed + n8n + turns + ai + missed;
    }

    public async Task<IReadOnlyList<AlertToMail>> UnsentAsync(CancellationToken ct)
    {
        var tasks = await (from a in db.PanelAlerts.AsNoTracking()
                           join t in db.StaffTasks.AsNoTracking() on a.TaskId equals (Guid?)t.Id
                           where a.EmailedAt == null && a.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1)
                                 // v1.105: never task_new. fix/alert-flood (the owner: «للعاجل والمتأخر بس»): the urgent and the
                                 // overdue only; a client asking for someone and the rest stay in the bell and Windows.
                                 && (a.Kind == AlertKinds.Urgent || a.Kind == AlertKinds.Overdue)
                                 && (a.Kind == AlertKinds.Overdue ? t.State == "overdue" : t.State != "done" && t.State != "cancelled")
                                 // v1.110 (AlertKinds.BellOnly): a paid urgent call's urgent alert while the lawyer's WhatsApp
                                 // alert is his, and its «late» one at every step
                                 && !(a.Kind == AlertKinds.Urgent && t.Type == PendingTask.UrgentCall)
                                 && !(a.Kind == AlertKinds.Overdue && UrgentCall.Contains(t.Type))
                           orderby a.Id
                           select new AlertToMail(a.Id, a.Kind, a.TaskId, t.Type, t.DueAt, t.StaffId, null)).ToListAsync(ct);
        var system = await db.PanelAlerts.AsNoTracking()
            .Where(a => a.Kind == AlertKinds.System && a.EmailedAt == null && a.CreatedAt > DateTimeOffset.UtcNow.AddDays(-1))
            .OrderBy(a => a.Id)
            .Select(a => new AlertToMail(a.Id, a.Kind, null, null, null, null, a.SystemWhat)).ToListAsync(ct);
        return [.. tasks, .. system];
    }

    /// <summary>An alert whose task was finished before its e-mail went out is never mailed.</summary>
    public Task<int> DropFinishedAsync(CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE hazem.panel_alerts a SET emailed_at = now()
          FROM hazem.staff_tasks t
         WHERE t.id = a.task_id AND a.emailed_at IS NULL
           AND (t.state IN ('done', 'cancelled') OR (a.kind = {AlertKinds.Overdue} AND t.state <> 'overdue'))
        """, ct);

    public Task MarkEmailedAsync(long id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.panel_alerts SET emailed_at = now() WHERE id = {id}", ct);

    private IQueryable<PanelUserRow> ActiveUsers => db.PanelUsers.AsNoTracking().Where(u => u.Active && u.Approval == PanelUser.ApprovalApproved);

    /// <summary>The office has no active staff account: the staff's alerts are the managers' (nobody would hear them otherwise).</summary>
    private async Task<bool> NoStaffAsync(CancellationToken ct) => !await ActiveUsers.AnyAsync(u => u.Role == Permissions.Staff, ct);

    public async Task<IReadOnlyList<string>> RecipientsAsync(AlertToMail alert, CancellationToken ct)
    {
        var audience = AlertAudience.For(alert.Kind, alert.TaskType);
        if (alert.StaffId is { } holder && audience != AlertAudience.Managers)
        {
            var theirs = await ActiveUsers.Where(u => u.StaffId == holder).Select(u => u.Email).ToListAsync(ct);
            if (theirs.Count > 0)
            {
                return theirs;
            }
            // a holder with no active account (left the office): the audience, so the alert is not lost
        }

        var roles = AlertAudience.Roles(audience, await NoStaffAsync(ct));
        return await ActiveUsers.Where(u => roles.Contains(u.Role)).Select(u => u.Email).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PanelAlert>> ListAsync(Viewer viewer, int limit, CancellationToken ct)
    {
        var rows = await (from a in Visible(viewer)
                          join t in db.StaffTasks.AsNoTracking() on a.TaskId equals (Guid?)t.Id into ts
                          from t in ts.DefaultIfEmpty()
                          join p in db.Persons.AsNoTracking() on t.PersonId equals p.Id into ps
                          from p in ps.DefaultIfEmpty()
                          join s in db.Staff.AsNoTracking() on t.StaffId equals s.Id into ss
                          from s in ss.DefaultIfEmpty()
                          join i in db.Intakes.AsNoTracking() on t.IntakeId equals i.Id into ii
                          from i in ii.DefaultIfEmpty()
                          orderby a.CreatedAt descending, a.Id descending
                          select new
                          {
                              a.Id, a.Kind, a.TaskId, Type = t == null ? null : t.Type, Name = p == null ? null : p.OfficeName,
                              DueAt = t == null ? (DateTimeOffset?)null : t.DueAt, a.CreatedAt, a.SystemWhat,
                              // v1.110: a finished task's alert is read for everyone (Finished)
                              Read = t != null && Finished.Contains(t.State) || db.PanelAlertReads.Any(r => r.AlertId == a.Id && r.UserId == viewer.UserId),
                              WaId = p == null ? null : p.WaId, StaffName = s == null ? null : s.Name, Matter = i == null ? null : i.MatterType,
                              a.ReceiptId,
                              // v1.112: the client's messages on the task (its own, and the ones that joined it)
                              Joined = t == null ? 0 : db.StaffTaskTurns.Count(x => x.TaskId == t.Id),
                          }).Take(limit).ToListAsync(ct);
        var receipts = await ReceiptsAsync([.. rows.Where(r => r.ReceiptId != null).Select(r => r.ReceiptId!.Value)], ct);
        // the Windows notification's details (v1.104): the number's last 3 digits only, as the task list shows them
        return rows.Select(r =>
        {
            var alert = new PanelAlert(r.Id, r.Kind, r.TaskId, r.Type, r.Name, r.DueAt, r.CreatedAt, r.Read, r.SystemWhat,
                r.WaId is { Length: >= 3 } wa ? wa[^3..] : null, r.Matter, r.StaffName)
            {
                Messages = r.TaskId is null ? null : 1 + r.Joined,
            };
            // v1.111: a receipt's alert, its client and what it pays for; read for everyone once it is reviewed
            return r.ReceiptId is { } id && receipts.TryGetValue(id, out var x)
                ? alert with
                {
                    ClientName = x.Name, PhoneLast3 = x.WaId is { Length: >= 3 } w ? w[^3..] : null, ReceiptFor = x.For, Amount = x.Amount,
                    Read = r.Read || x.ReviewedSince(r.CreatedAt),
                }
                : alert;
        }).ToList();
    }

    private sealed record ReceiptAbout(string? Name, string? WaId, string? For, int Amount, string State, DateTimeOffset? ReviewedAt)
    {
        /// <summary>Reviewed since the alert was made, or no longer waiting (refunded, the booking cancelled…).</summary>
        public bool ReviewedSince(DateTimeOffset made) => ReviewedAt >= made || !Waiting.Contains(State);
    }

    /// <summary>v1.111: who sent each receipt and what it pays for (an appointment, or a written service).</summary>
    private async Task<Dictionary<Guid, ReceiptAbout>> ReceiptsAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        var rows = await (from r in db.PaymentReceipts.AsNoTracking()
                          join pay in db.Payments.AsNoTracking() on r.PaymentId equals pay.Id
                          join ap in db.Appointments.AsNoTracking() on pay.AppointmentId equals (Guid?)ap.Id into aps
                          from ap in aps.DefaultIfEmpty()
                          join so in db.ServiceOrders.AsNoTracking() on pay.ServiceOrderId equals (Guid?)so.Id into sos
                          from so in sos.DefaultIfEmpty()
                          where ids.Contains(r.Id)
                          select new
                          {
                              r.Id, Person = ap != null ? (Guid?)ap.PersonId : so != null ? (Guid?)so.PersonId : null,
                              For = ap != null ? ap.Service : so != null ? so.Kind : null, pay.AmountDue, pay.State, pay.ReviewedAt,
                          }).ToListAsync(ct);
        var people = rows.Where(r => r.Person != null).Select(r => r.Person!.Value).Distinct().ToArray();
        var who = await db.Persons.AsNoTracking().Where(p => people.Contains(p.Id))
            .Select(p => new { p.Id, p.OfficeName, p.WaId }).ToDictionaryAsync(p => p.Id, ct);
        return rows.ToDictionary(r => r.Id, r =>
        {
            var p = r.Person is { } id && who.TryGetValue(id, out var found) ? found : null;
            return new ReceiptAbout(p?.OfficeName, p?.WaId, r.For, r.AmountDue, r.State, r.ReviewedAt);
        });
    }

    /// <summary>v1.111: the receipt alerts whose payment was reviewed since they were made (read for everyone).</summary>
    private IQueryable<long> ReviewedReceiptAlerts() =>
        from a in db.PanelAlerts.AsNoTracking()
        join r in db.PaymentReceipts.AsNoTracking() on a.ReceiptId equals (Guid?)r.Id
        join pay in db.Payments.AsNoTracking() on r.PaymentId equals pay.Id
        where pay.ReviewedAt >= a.CreatedAt || !Waiting.Contains(pay.State)
        select a.Id;

    public Task<int> UnreadAsync(Viewer viewer, CancellationToken ct)
    {
        var reviewed = ReviewedReceiptAlerts(); // v1.111
        return Visible(viewer).CountAsync(a => !db.PanelAlertReads.Any(r => r.AlertId == a.Id && r.UserId == viewer.UserId)
                                               && !db.StaffTasks.Any(t => t.Id == a.TaskId && Finished.Contains(t.State))
                                               && !reviewed.Contains(a.Id), ct);
    }

    public Task<int> UnreadSystemAsync(Viewer viewer, CancellationToken ct) =>
        Visible(viewer).CountAsync(a => a.Kind == AlertKinds.System && !db.PanelAlertReads.Any(r => r.AlertId == a.Id && r.UserId == viewer.UserId), ct);

    /// <summary>
    /// v1.106: the areas of the office's data the panel shows, and the rows each one is read from. The panel's
    /// shared/api/alerts.ts says which pages each area asks again: a new area goes in both.
    /// A row's xmin is the transaction that wrote its current version: an insert or an update gives it a new one and a
    /// removal drops one, so an area's count and sum of xmin move with any change, from the panel, Hazem, n8n or a job.
    /// </summary>
    private static readonly (string Area, string[] Parts)[] DataAreas =
    [
        ("tasks", [Rows("staff_tasks")]),
        ("appointments", [Rows("appointments")]),
        ("payments", [Rows("payments"), Rows("payment_receipts")]),
        ("services", [Rows("service_orders"), Rows("service_files"), Rows("service_ratings")]),
        ("urf", [Rows("urf_cases"), Rows("urf_files")]),
        // a client's own row changes with his messages (his WhatsApp name): only a new client counts there
        ("clients", [Count("persons"), Rows("intakes")]),
        ("ai", [Count("ai_usage")]), // only ever added to
        ("lists", [Rows("list_entries")]),
        ("muted", [Rows("conversations", "state = 'muted'")]),
        ("texts", [Rows("approved_texts")]),
        ("settings", [Rows("settings")]), // the prices, the booking's settings and the office's data
        ("booking", [Rows("booking_weekly_hours"), Rows("booking_exceptions"), Rows("booking_changes")]),
        ("users", [Rows("panel_users"), Rows("staff")]),
    ];

    private static readonly string DataStampSql = "SELECT concat_ws(';', "
        + string.Join(", ", DataAreas.Select(a => $"'{a.Area}=' || concat_ws(',', {string.Join(", ", a.Parts)})"))
        + ") AS \"Value\"";

    private static string Rows(string table, string? where = null) =>
        $"(SELECT count(*)::text || ':' || coalesce(sum(xmin::text::bigint), 0)::text FROM hazem.{table}{(where is null ? "" : " WHERE " + where)})";

    private static string Count(string table) => $"(SELECT count(*)::text FROM hazem.{table})";

    public async Task<IReadOnlyDictionary<string, string>> DataStampsAsync(CancellationToken ct)
    {
        var line = await db.Database.SqlQueryRaw<string>(DataStampSql).SingleAsync(ct);
        return line.Split(';').Select(a => a.Split('=', 2)).ToDictionary(a => a[0], a => a[1], StringComparer.Ordinal);
    }

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

    /// <summary>
    /// fix/alert-flood (the owner, v1.108: «التنبيه يروح للي عليه الدور بس»): the alerts that are this person's job
    /// (<see cref="AlertAudience"/>), not every alert of a task they may see. A held task's alerts are its holder's. A low
    /// rating's and the system's are the managers' (TASK 13, v1.31). With no active staff account, the staff's are the managers'.
    /// </summary>
    private IQueryable<PanelAlertRow> Visible(Viewer viewer)
    {
        var audience = AlertAudience.Of(viewer.Role);
        var managers = audience == AlertAudience.Managers;
        var lawyer = audience == AlertAudience.Lawyer;
        var office = audience == AlertAudience.Office
                     || (managers && !db.PanelUsers.Any(u => u.Active && u.Approval == PanelUser.ApprovalApproved && u.Role == Permissions.Staff));
        var staff = viewer.StaffId;
        string[] lawyerWork = [.. AlertAudience.LawyerWork];
        // v1.111: a receipt's alert is the staff's (B-05), a written service's the lawyer's, who confirms it himself (B-16)
        var services = from r in db.PaymentReceipts.AsNoTracking()
                       join pay in db.Payments.AsNoTracking() on r.PaymentId equals pay.Id
                       where pay.ServiceOrderId != null
                       select (Guid?)r.Id;
        return from a in db.PanelAlerts.AsNoTracking()
               join t in db.StaffTasks.AsNoTracking() on a.TaskId equals (Guid?)t.Id into ts
               from t in ts.DefaultIfEmpty()
               where a.Kind == AlertKinds.Receipt
                   ? services.Contains(a.ReceiptId) ? lawyer : office
                   : t == null
                   ? a.Kind == AlertKinds.System && managers
                   : a.Kind == AlertKinds.Rating || t.Type == AlertAudience.RatingType
                       ? managers
                       : t.StaffId != null && db.PanelUsers.Any(u => u.StaffId == t.StaffId && u.Active)
                           ? staff != null && t.StaffId == staff
                           : a.Kind == AlertKinds.Urgent ? lawyer || office
                           : lawyerWork.Contains(t.Type) ? lawyer : office
               select a;
    }
}

/// <summary>
/// Every 3 seconds (Alerts:IntervalSeconds; every 10 from v1.104 to v1.105, every minute before): tasks past their due time become overdue
/// (S-05), and new alerts are made and mailed. The open panels hear them at once (<see cref="AlertSignal"/>). The system's
/// checks stay once a minute, because a client's request comes first (the owner, 2026-11-12). v1.106: every round also
/// looks which areas of the office's data changed, and wakes the open panels the same way (<see cref="DataSignal"/>). Off in tests
/// (Alerts:RunWorker=false): they call the steps themselves. v1.107: every round is counted (<see cref="AlertMetrics"/>), so the
/// monitoring's e-mails say when the rounds stop or the e-mails fail.
/// </summary>
public sealed class AlertWorker(IServiceScopeFactory scopes, IConfiguration config, AlertMetrics metrics, ILogger<AlertWorker> log)
    : BackgroundService
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Alerts:RunWorker", true))
        {
            return;
        }

        var panelUrl = config["Panel:PublicUrl"] is { Length: > 0 } u ? u : "http://127.0.0.1:8080";
        // v1.106 (the owner: «خليها 3 ثواني»): every 3 seconds, so the bell and every open page hear within 3
        var every = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Alerts:IntervalSeconds", 3), 1, 60));
        DateTimeOffset systemAt = DateTimeOffset.MinValue, warnedAt = DateTimeOffset.MinValue;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var system = DateTimeOffset.UtcNow - systemAt >= Minute;
                if (system)
                {
                    systemAt = DateTimeOffset.UtcNow;
                }

                var overdue = await scope.ServiceProvider.GetRequiredService<Lists.TaskMaintenance>().MarkOverdueAsync(stop);
                var alerts = scope.ServiceProvider.GetRequiredService<PanelAlerts>();
                await alerts.WatchDataAsync(stop); // v1.106: before the e-mails, which may take a while
                var (made, mailed, failed, error) = await alerts.RunAsync(panelUrl, stop, system);
                metrics.RoundOk(mailed, failed, DateTimeOffset.UtcNow);
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
                metrics.RoundFailed();
                // a round every few seconds: one warning a minute at most while it keeps failing
                if (DateTimeOffset.UtcNow - warnedAt >= Minute)
                {
                    warnedAt = DateTimeOffset.UtcNow;
                    log.LogWarning("Alerts round failed: {Kind}: {Message}", e.GetType().Name, e.Message);
                }
            }

            try
            {
                await Task.Delay(every, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
