using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Conversations;
using Office.Application.Panel;
using Office.Application.Urf;
using Office.Domain.Tasks;
using Office.Infrastructure.Booking;
using Office.Infrastructure.Panel;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Urf;

// TASK 11 (DECISIONS v1.15, B-11 / S-06): the Urfi cases from the panel. Every role sees them and opens their documents
// (recorded, like 07c); only the lawyer and the manager approve or refuse one before the appointment. What the client
// must hear is sent by the worker (UrfNotices).

public sealed record UrfCaseView(Guid Id, string Kind, string State, string? Name, string WaId, DateTimeOffset CreatedAt,
    int? HusbandAge, int? WifeAge, string? WifePast, string? Guardian, string? Witnesses, bool? HasCopy, bool? Agreed, bool? Children,
    string Documents, int Files, Guid? AppointmentId, DateTimeOffset? Start, string? Appointment, string? Payment, string? DecidedBy,
    DateTimeOffset? DecidedAt, string? Notice);

public sealed class PanelUrf(HazemDbContext db, BookingStore store, TimeProvider clock)
{
    public const string ErrInvalid = "invalid";
    public const string ErrNotFound = "not_found";
    public const string ErrNotAllowed = "not_allowed";

    /// <summary>open: the cases still running (the oldest first); done: the last 60 days decided or closed.</summary>
    public async Task<IReadOnlyList<UrfCaseView>?> ListAsync(string? show, CancellationToken ct)
    {
        if (show is not (null or "open" or "done"))
        {
            return null;
        }

        var open = show != "done";
        var since = clock.GetUtcNow().AddDays(-60).ToUniversalTime();
        var rows = await (from c in db.UrfCases.AsNoTracking()
                          join p in db.Persons.AsNoTracking() on c.PersonId equals p.Id
                          join a in db.Appointments.AsNoTracking() on c.AppointmentId equals (Guid?)a.Id into apps
                          from a in apps.DefaultIfEmpty()
                          join pay in db.Payments.AsNoTracking() on c.AppointmentId equals pay.AppointmentId into pays
                          from pay in pays.DefaultIfEmpty()
                          join u in db.PanelUsers.AsNoTracking() on c.DecidedBy equals (Guid?)u.Id into users
                          from u in users.DefaultIfEmpty()
                          where open
                              ? c.State == UrfCase.Collecting || c.State == UrfCase.DocumentsPending || c.State == UrfCase.Booked
                              : c.State != UrfCase.Collecting && c.State != UrfCase.DocumentsPending && c.State != UrfCase.Booked && c.UpdatedAt >= since
                          orderby c.CreatedAt
                          select new
                          {
                              c, NameRaw = p.OfficeName, p.WaId, Files = db.UrfFiles.Count(f => f.CaseId == c.Id),
                              Start = a == null ? (DateTimeOffset?)null : a.StartsAt, Appointment = a == null ? null : a.State,
                              Payment = pay == null ? null : pay.State, DecidedBy = u == null ? null : u.DisplayName,
                          }).Take(300).ToListAsync(ct);
        return rows.Select(r => new UrfCaseView(r.c.Id, r.c.Kind, r.c.State, r.NameRaw, r.WaId, r.c.CreatedAt, r.c.HusbandAge, r.c.WifeAge,
            r.c.WifePast, r.c.Guardian, r.c.Witnesses, r.c.HasCopy, r.c.Agreed, r.c.Children, UrfStore.ToCase(r.c).Documents(), r.Files,
            r.c.AppointmentId, r.Start, r.Appointment, r.Payment, r.DecidedBy, r.c.DecidedAt, r.c.ClientNotice)).ToList();
    }

    /// <summary>The n-th document the client sent for the case (0 = the first), for the panel to show.</summary>
    public async Task<ClientFileRef?> FileAsync(Guid caseId, int index, CancellationToken ct)
    {
        if (index < 0)
        {
            return null;
        }

        var f = await db.UrfFiles.AsNoTracking().Where(x => x.CaseId == caseId).OrderBy(x => x.ReceivedAt).ThenBy(x => x.Id)
            .Skip(index).FirstOrDefaultAsync(ct);
        return f is null ? null : ClientFileRef.Of(f.Bucket, f.Key, f.Mime, f.Filename);
    }

    public Task RecordFileOpenedAsync(Guid by, Guid caseId, int index, CancellationToken ct) =>
        PanelAudit.WriteAsync(db, clock, by, "urf.file_open", "urf", caseId,
            new Dictionary<string, string?> { ["index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct);

    /// <summary>
    /// B-11.6: the lawyer's (or the manager's) decision before the appointment. Refused: the appointment is cancelled by
    /// the office and what was paid is due back in full, with a task for the person who transfers it (P2).
    /// </summary>
    public async Task<string?> ReviewAsync(Guid by, Guid caseId, string? decision, CancellationToken ct)
    {
        if (decision is not ("approved" or "rejected"))
        {
            return ErrInvalid;
        }

        var json = await db.Database.SqlQuery<string>($"""
            SELECT hazem.review_urf_case({caseId}, {decision}, {by}, {UrfRules.PolicyVersion})::text AS "Value"
            """).SingleAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        switch (r.GetProperty("status").GetString())
        {
            case "approved" or "rejected":
                break;
            case "not_found":
                return ErrNotFound;
            case "invalid":
                return ErrInvalid;
            default:
                return ErrNotAllowed;
        }

        if (r.GetProperty("refund").ValueKind == JsonValueKind.Number)
        {
            await store.AddTaskAsync(r.GetProperty("person").GetGuid(), $"urf:{caseId:N}:refund", PendingTask.Refund,
                OfficeHours.DueAt(clock.GetUtcNow()), ct);
        }

        await PanelAudit.WriteAsync(db, clock, by, "urf." + decision, "urf", caseId, new Dictionary<string, string?>(), ct);
        return null;
    }
}
