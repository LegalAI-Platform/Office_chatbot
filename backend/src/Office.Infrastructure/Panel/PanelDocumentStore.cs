using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Panel;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Panel;

/// <summary>
/// v1.110: «المستندات» in the database (hazem.client_documents, migration 0079). A page of the clients with files, the one
/// whose latest file is the newest first, then their newest files. A search names the clients as the search bar does
/// (<see cref="PanelSearchStore.PersonIdsAsync"/>). Only where a file is and whose it is; its content is read by the API.
/// </summary>
public sealed class PanelDocumentStore(HazemDbContext db, TimeProvider clock) : IPanelDocumentStore
{
    public async Task<DocumentPage> ListAsync(SearchTerm term, string? kind, string? source, bool allClients, Guid? staffId, int page,
        int pageSize, int perClient, CancellationToken ct)
    {
        var anyone = term.IsEmpty;
        Guid[] persons = anyone ? [] : [.. await PanelSearchStore.PersonIdsAsync(db, term, ct)];
        if (!anyone && persons.Length == 0)
        {
            return new([], 0, page, pageSize);
        }

        var offset = (page - 1) * pageSize;
        // a staff member: the clients of her tasks and of the unassigned ones (PanelTasks.MaySee)
        var clients = await db.Database.SqlQuery<ClientRow>($"""
            SELECT d.person_id AS "Id", p.office_name AS "Name", right(p.wa_id, 3) AS "Last3", count(*)::int AS "Count",
                   max(d.received_at) AS "LatestAt", (count(*) OVER ())::int AS "Total"
              FROM hazem.client_documents d JOIN hazem.persons p ON p.id = d.person_id
             WHERE ({anyone} OR d.person_id = ANY({persons}))
               AND ({kind}::text IS NULL OR d.kind = {kind}::text) AND ({source}::text IS NULL OR d.source = {source}::text)
               AND ({allClients} OR EXISTS (SELECT 1 FROM hazem.staff_tasks st
                                             WHERE st.person_id = d.person_id
                                               AND (st.staff_id IS NULL OR st.staff_id = {staffId}::uuid)))
             GROUP BY d.person_id, p.office_name, p.wa_id
             ORDER BY max(d.received_at) DESC, d.person_id
             LIMIT {pageSize} OFFSET {offset}
            """).ToListAsync(ct);
        if (clients.Count == 0)
        {
            return new([], 0, page, pageSize);
        }

        Guid[] shown = [.. clients.Select(c => c.Id)];
        var files = await db.Database.SqlQuery<FileRow>($"""
            SELECT x."Source", x."Id", x."Index", x."PersonId", x."Kind", x."Mime", x."Name", x."ReceivedAt", x."TaskId" FROM (
              SELECT d.source AS "Source", d.id AS "Id", d.index AS "Index", d.person_id AS "PersonId", d.kind AS "Kind",
                     d.mime AS "Mime", d.name AS "Name", d.received_at AS "ReceivedAt",
                     (SELECT st.id FROM hazem.staff_tasks st WHERE st.turn_id = d.turn_id ORDER BY st.created_at LIMIT 1) AS "TaskId",
                     row_number() OVER (PARTITION BY d.person_id ORDER BY d.received_at DESC, d.id, d.index) AS n
                FROM hazem.client_documents d
               WHERE d.person_id = ANY({shown})
                 AND ({kind}::text IS NULL OR d.kind = {kind}::text) AND ({source}::text IS NULL OR d.source = {source}::text)) x
             WHERE x.n <= {perClient}
             ORDER BY x."ReceivedAt" DESC, x."Id", x."Index"
            """).ToListAsync(ct);
        var byClient = files.ToLookup(f => f.PersonId);
        return new(
            [.. clients.Select(c => new DocumentClient(c.Id, c.Name, c.Last3, c.Count, c.LatestAt,
                [.. byClient[c.Id].Select(f => new DocumentFile(f.Source, f.Id, f.Index, f.Kind, f.Mime, f.Name, f.ReceivedAt, f.TaskId))]))],
            clients[0].Total, page, pageSize);
    }

    public async Task<(ClientFileRef File, Guid PersonId)?> FileAsync(string source, Guid id, int index, CancellationToken ct)
    {
        switch (source)
        {
            case DocumentSources.Message:
                var turn = await (from t in db.Turns.AsNoTracking()
                                  join p in db.Persons.AsNoTracking() on t.WaId equals p.WaId
                                  where t.Id == id
                                  select new { t.Messages, PersonId = p.Id }).FirstOrDefaultAsync(ct);
                return turn is not null && MessageFile(turn.Messages, index) is { } file ? (file, turn.PersonId) : null;
            case DocumentSources.Receipt:
                var receipt = await (from r in db.PaymentReceipts.AsNoTracking()
                                     join pay in db.Payments.AsNoTracking() on r.PaymentId equals pay.Id
                                     join a in db.Appointments.AsNoTracking() on pay.AppointmentId equals (Guid?)a.Id into aa
                                     from a in aa.DefaultIfEmpty()
                                     join o in db.ServiceOrders.AsNoTracking() on pay.ServiceOrderId equals (Guid?)o.Id into oo
                                     from o in oo.DefaultIfEmpty()
                                     where r.Id == id
                                     select new { r.Bucket, r.Key, r.Mime, PersonId = a != null ? a.PersonId : o != null ? o.PersonId : (Guid?)null })
                    .FirstOrDefaultAsync(ct);
                return receipt is { PersonId: { } owner } ? (ClientFileRef.Of(receipt.Bucket, receipt.Key, receipt.Mime, null), owner) : null;
            case DocumentSources.Service:
                var order = await (from f in db.ServiceFiles.AsNoTracking()
                                   join o in db.ServiceOrders.AsNoTracking() on f.OrderId equals o.Id
                                   where f.Id == id
                                   select new { f.Bucket, f.Key, f.Mime, f.Filename, o.PersonId }).FirstOrDefaultAsync(ct);
                return order is null ? null : (ClientFileRef.Of(order.Bucket, order.Key, order.Mime, order.Filename), order.PersonId);
            case DocumentSources.Urf:
                var urf = await (from f in db.UrfFiles.AsNoTracking()
                                 join c in db.UrfCases.AsNoTracking() on f.CaseId equals c.Id
                                 where f.Id == id
                                 select new { f.Bucket, f.Key, f.Mime, f.Filename, c.PersonId }).FirstOrDefaultAsync(ct);
                return urf is null ? null : (ClientFileRef.Of(urf.Bucket, urf.Key, urf.Mime, urf.Filename), urf.PersonId);
            default:
                return null;
        }
    }

    /// <summary>The picture or document of the n-th message of a stored turn (as hazem.client_documents lists them).</summary>
    private static ClientFileRef? MessageFile(string messages, int index)
    {
        using var doc = JsonDocument.Parse(messages);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || index < 0 || index >= doc.RootElement.GetArrayLength())
        {
            return null;
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var m = doc.RootElement[index];
        return Str(m, "type") is "image" or "document" && m.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object
               && Str(media, "status") == "stored" && Str(media, "bucket") is { } bucket && Str(media, "key") is { } key
            ? new ClientFileRef(Str(m, "type")!, bucket, key, Str(media, "mime_type"), Str(media, "filename"))
            : null;
    }

    public Task<bool> StaffMaySeeAsync(Guid personId, Guid? staffId, CancellationToken ct) =>
        db.StaffTasks.AsNoTracking().AnyAsync(t => t.PersonId == personId && (t.StaffId == null || t.StaffId == staffId), ct);

    /// <summary>Who opened a client's file, as a receipt's opening is (PanelPayments.RecordOpenedAsync): only where it came from.</summary>
    public Task RecordOpenedAsync(Guid by, string source, Guid id, CancellationToken ct) =>
        PanelAudit.WriteAsync(db, clock, by, "document.open", null, null,
            new Dictionary<string, string?> { ["source"] = source, ["id"] = id.ToString() }, ct);

    public sealed class ClientRow
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Last3 { get; set; }
        public int Count { get; set; }
        public DateTimeOffset LatestAt { get; set; }
        public int Total { get; set; }
    }

    public sealed class FileRow
    {
        public string Source { get; set; } = "";
        public Guid Id { get; set; }
        public int Index { get; set; }
        public Guid PersonId { get; set; }
        public string Kind { get; set; } = "";
        public string? Mime { get; set; }
        public string? Name { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public Guid? TaskId { get; set; }
    }
}
