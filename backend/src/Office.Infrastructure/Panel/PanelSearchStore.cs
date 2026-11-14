using Microsoft.EntityFrameworkCore;
using Office.Application.Panel;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Panel;

/// <summary>
/// v1.110 (the owner, 2026-11-14: «السيرش بار هنا حاسه بيسرش غلط» … «اعمل بحث شامل»): the panel's search in the database. A
/// client is matched once (<see cref="PersonIdsAsync"/>): every word of the term in his name through hazem.search_text
/// (migration 0078), every number as the end of his WhatsApp number. His appointments and the tasks the viewer may see
/// follow. Only the office's name for him and his number's last 3 digits leave the database, never the number.
/// </summary>
public sealed class PanelSearchStore(HazemDbContext db, TimeProvider clock) : IPanelSearchStore
{
    /// <summary>More clients than this for one term is no search any more (a single letter): the first ones only.</summary>
    public const int MaxClients = 200;

    /// <summary>The clients the term names: also the tasks page's search, so both find the same people.</summary>
    public static async Task<IReadOnlyList<Guid>> PersonIdsAsync(HazemDbContext db, SearchTerm term, CancellationToken ct)
    {
        string[] words = [.. term.Words];
        string[] digits = [.. term.Digits];
        return await db.Database.SqlQuery<Guid>($"""
            SELECT p.id AS "Value" FROM hazem.persons p
             WHERE NOT EXISTS (SELECT 1 FROM unnest({words}::text[]) w
                                WHERE strpos(hazem.search_text(coalesce(p.office_name, '') || ' ' || coalesce(p.whatsapp_name, '')),
                                             hazem.search_text(w)) = 0)
               AND NOT EXISTS (SELECT 1 FROM unnest({digits}::text[]) d WHERE right(p.wa_id, length(d)) <> d)
             LIMIT {MaxClients}
            """).ToListAsync(ct);
    }

    public async Task<SearchResults> SearchAsync(SearchTerm term, bool allTasks, Guid? staffId, bool appointments, int each, CancellationToken ct)
    {
        Guid[] persons = [.. await PersonIdsAsync(db, term, ct)];
        if (persons.Length == 0)
        {
            return SearchResults.Empty;
        }

        var now = clock.GetUtcNow().ToUniversalTime(); // the database takes UTC only
        // the tasks this viewer may see (PanelTasks.MaySee): every one, or theirs and the unassigned; the unfinished first
        var tasks = await db.Database.SqlQuery<TaskHit>($"""
            SELECT t.id AS "Id", p.office_name AS "Name", right(p.wa_id, 3) AS "Last3", t.type AS "Type", t.state AS "State",
                   t.due_at AS "DueAt"
              FROM hazem.staff_tasks t JOIN hazem.persons p ON p.id = t.person_id
             WHERE t.person_id = ANY({persons}) AND ({allTasks} OR t.staff_id IS NULL OR t.staff_id = {staffId})
             ORDER BY t.state IN ('open', 'in_progress', 'overdue') DESC, t.created_at DESC
             LIMIT {each}
            """).ToListAsync(ct);
        // the coming ones first (the nearest first), then the past ones (the latest first); a hold that ran out is no appointment
        List<AppointmentHit> booked = !appointments ? [] : await db.Database.SqlQuery<AppointmentHit>($"""
            SELECT a.id AS "Id", p.office_name AS "Name", right(p.wa_id, 3) AS "Last3", a.service AS "Service", a.state AS "State",
                   a.starts_at AS "StartsAt"
              FROM hazem.appointments a JOIN hazem.persons p ON p.id = a.person_id
             WHERE a.person_id = ANY({persons}) AND a.state <> 'expired'
             ORDER BY a.starts_at >= {now} DESC, CASE WHEN a.starts_at >= {now} THEN a.starts_at END, a.starts_at DESC
             LIMIT {each}
            """).ToListAsync(ct);
        // a client the viewer has something to open for: a task he may see, or an appointment
        var clients = await db.Database.SqlQuery<ClientHit>($"""
            SELECT p.id AS "Id", p.office_name AS "Name", right(p.wa_id, 3) AS "Last3",
                   (SELECT count(*)::int FROM hazem.staff_tasks t
                     WHERE t.person_id = p.id AND t.state IN ('open', 'in_progress', 'overdue')
                       AND ({allTasks} OR t.staff_id IS NULL OR t.staff_id = {staffId})) AS "OpenTasks",
                   (SELECT t.id FROM hazem.staff_tasks t
                     WHERE t.person_id = p.id AND ({allTasks} OR t.staff_id IS NULL OR t.staff_id = {staffId})
                     ORDER BY t.state IN ('open', 'in_progress', 'overdue') DESC, t.created_at DESC LIMIT 1) AS "TaskId",
                   (SELECT a.starts_at FROM hazem.appointments a
                     WHERE {appointments} AND a.person_id = p.id AND a.state <> 'expired'
                     ORDER BY a.starts_at >= {now} DESC, CASE WHEN a.starts_at >= {now} THEN a.starts_at END, a.starts_at DESC
                     LIMIT 1) AS "AppointmentAt"
              FROM hazem.persons p
             WHERE p.id = ANY({persons})
               AND (EXISTS (SELECT 1 FROM hazem.staff_tasks t
                             WHERE t.person_id = p.id AND ({allTasks} OR t.staff_id IS NULL OR t.staff_id = {staffId}))
                    OR {appointments} AND EXISTS (SELECT 1 FROM hazem.appointments a WHERE a.person_id = p.id AND a.state <> 'expired'))
             ORDER BY p.office_name NULLS LAST, p.id
             LIMIT {each}
            """).ToListAsync(ct);

        return new(
            [.. clients.Select(c => new SearchClient(c.Id, c.Name, c.Last3, c.OpenTasks, c.TaskId, c.AppointmentAt))],
            [.. booked.Select(a => new SearchAppointment(a.Id, a.Name, a.Last3, a.Service, a.State, a.StartsAt))],
            [.. tasks.Select(t => new SearchTask(t.Id, t.Name, t.Last3, t.Type, t.State, t.DueAt))]);
    }

    public sealed class TaskHit
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Last3 { get; set; }
        public string Type { get; set; } = "";
        public string State { get; set; } = "";
        public DateTimeOffset DueAt { get; set; }
    }

    public sealed class AppointmentHit
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Last3 { get; set; }
        public string Service { get; set; } = "";
        public string State { get; set; } = "";
        public DateTimeOffset StartsAt { get; set; }
    }

    public sealed class ClientHit
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Last3 { get; set; }
        public int OpenTasks { get; set; }
        public Guid? TaskId { get; set; }
        public DateTimeOffset? AppointmentAt { get; set; }
    }
}
