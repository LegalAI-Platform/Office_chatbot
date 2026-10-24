using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Conversations;
using Office.Application.Panel;
using Office.Domain.Tasks;
using Office.Infrastructure.Booking;
using Office.Infrastructure.Panel;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Services;

// TASK 10 (DECISIONS v1.14, B-16 / B-17 / S-07): the service orders from the panel. Every role sees them, opens
// the client's documents (recorded, like 07c), uploads and delivers the file, and closes an order; only the lawyer
// and the manager price one or ask for a written opinion. What the client must hear is sent by the worker.

public sealed record ServiceOrderView(Guid Id, string Kind, string State, string? Name, string WaId, DateTimeOffset CreatedAt, int Files,
    int? Price, bool PriceException, DateOnly? DueOn, string? Delivery, DateTimeOffset? PriceUntil, string? Payment, string? FileName,
    DateTimeOffset? DeliveredAt, string? ClosedReason, Guid? AppointmentId, string? Notice);

public sealed class PanelServices(HazemDbContext db, BookingStore store, IDeliverables deliverables, TimeProvider clock)
{
    public const string ErrInvalid = "invalid";
    public const string ErrNotFound = "not_found";
    public const string ErrNotAllowed = "not_allowed";
    public const string ErrOutOfRange = "out_of_range";
    public const string ErrNoFile = "no_file";
    public const string ErrTooLarge = "too_large";
    public const string ErrNotConfigured = "not_configured";

    /// <summary>B-16.6: what may be delivered: a PDF or a Word file, 20 MB at most.</summary>
    public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    public const long MaxBytes = 20 * 1024 * 1024;

    /// <summary>open: everything not delivered or closed (the oldest first); done: the last 60 days delivered or closed.</summary>
    public async Task<IReadOnlyList<ServiceOrderView>?> ListAsync(string? show, CancellationToken ct)
    {
        if (show is not (null or "open" or "done"))
        {
            return null;
        }

        var since = clock.GetUtcNow().AddDays(-60).ToUniversalTime();
        var open = show != "done";
        return await (from o in db.ServiceOrders.AsNoTracking()
                      join p in db.Persons.AsNoTracking() on o.PersonId equals p.Id
                      join pay in db.Payments.AsNoTracking() on (Guid?)o.Id equals pay.ServiceOrderId into pays
                      from pay in pays.DefaultIfEmpty()
                      where open ? o.State != "delivered" && o.State != "closed" : (o.State == "delivered" || o.State == "closed") && o.UpdatedAt >= since
                      orderby o.CreatedAt
                      select new ServiceOrderView(o.Id, o.Kind, o.State, p.NameRaw, p.WaId, o.CreatedAt, db.ServiceFiles.Count(f => f.OrderId == o.Id),
                          o.Price, o.PriceException, o.DueOn, o.Delivery, o.PriceUntil, pay == null ? null : pay.State, o.FileName, o.DeliveredAt,
                          o.ClosedReason, o.AppointmentId, o.ClientNotice)).Take(300).ToListAsync(ct);
    }

    /// <summary>The n-th document the client sent for the order (0 = the first), for the panel to show.</summary>
    public async Task<ClientFileRef?> FileAsync(Guid orderId, int index, CancellationToken ct)
    {
        if (index < 0)
        {
            return null;
        }

        var f = await db.ServiceFiles.AsNoTracking().Where(x => x.OrderId == orderId).OrderBy(x => x.ReceivedAt).ThenBy(x => x.Id)
            .Skip(index).FirstOrDefaultAsync(ct);
        return f is null ? null : ClientFileRef.Of(f.Bucket, f.Key, f.Mime, f.Filename);
    }

    public Task RecordFileOpenedAsync(Guid by, Guid orderId, int index, CancellationToken ct) =>
        Audit(by, "service.file_open", orderId, new Dictionary<string, string?> { ["index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct);

    /// <summary>B-16.3 (B-10): the price, the delivery day and how; outside the kind's range only as a confirmed exception.</summary>
    public async Task<string?> PriceAsync(Guid by, Guid id, int? price, bool exception, DateOnly? dueOn, string? delivery, CancellationToken ct)
    {
        var status = await StatusAsync($"""
            SELECT hazem.price_service_order({id}, {price}, {exception}, {dueOn}, {delivery}, {by})::text AS "Value"
            """, ct);
        if (status != "priced")
        {
            return status switch
            {
                "not_found" => ErrNotFound,
                "not_allowed" => ErrNotAllowed,
                "out_of_range" => ErrOutOfRange,
                _ => ErrInvalid,
            };
        }

        await Audit(by, "service.priced", id, new Dictionary<string, string?>(), ct);
        return null;
    }

    /// <summary>B-17: «مطلوب رأي مكتوب» on a consultation that took place: an order waiting for its 2,000.</summary>
    public async Task<string?> OpinionAsync(Guid by, Guid appointmentId, DateOnly? dueOn, string? delivery, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT hazem.open_written_opinion({appointmentId}, {dueOn}, {delivery}, {by})::text AS "Value"
            """).SingleAsync(ct);
        using var doc = JsonDocument.Parse(json);
        switch (doc.RootElement.GetProperty("status").GetString())
        {
            case "opened":
                await Audit(by, "service.opinion", doc.RootElement.GetProperty("id").GetGuid(), new Dictionary<string, string?>(), ct);
                return null;
            case "not_found":
                return ErrNotFound;
            case "invalid":
                return ErrInvalid;
            default:
                return ErrNotAllowed;
        }
    }

    /// <summary>B-16.6 (I1): a signed upload for the file to deliver; the bytes never come through the API.</summary>
    public async Task<(string? Error, string? Url, string? Key, string? ContentType)> UploadAsync(Guid id, string? filename, CancellationToken ct)
    {
        if (!deliverables.Configured)
        {
            return (ErrNotConfigured, null, null, null);
        }

        var ext = Path.GetExtension(filename ?? "");
        if (string.IsNullOrWhiteSpace(filename) || filename.Length > 120 || !Types.TryGetValue(ext, out var type))
        {
            return (ErrInvalid, null, null, null);
        }

        var o = await db.ServiceOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null)
        {
            return (ErrNotFound, null, null, null);
        }

        if (o.State != "in_progress" || o.Delivery != "file")
        {
            return (ErrNotAllowed, null, null, null);
        }

        var (url, key) = deliverables.UploadLink(id, filename, type);
        return (null, url, key, type);
    }

    /// <summary>B-16.6: delivered. A file must be in storage under the order (checked, never read); a call needs nothing.</summary>
    public async Task<string?> DeliverAsync(Guid by, Guid id, string? key, string? filename, CancellationToken ct)
    {
        var o = await db.ServiceOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null)
        {
            return ErrNotFound;
        }

        string? mime = null;
        if (o.Delivery == "file")
        {
            if (!deliverables.Configured)
            {
                return ErrNotConfigured;
            }

            if (key is null || !key.StartsWith($"{S3Deliverables.Prefix}{id}/", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(filename)
                || filename.Length > 120 || !Types.TryGetValue(Path.GetExtension(filename), out mime)
                || !string.Equals(Path.GetExtension(filename), Path.GetExtension(key), StringComparison.OrdinalIgnoreCase)) // the type of the file uploaded
            {
                return ErrInvalid;
            }

            var stored = await deliverables.HeadAsync(key, ct);
            if (stored is null)
            {
                return ErrNoFile;
            }

            if (stored.Size > MaxBytes)
            {
                return ErrTooLarge;
            }
        }

        var status = await StatusAsync($"""
            SELECT hazem.deliver_service_order({id}, {by}, {key}, {filename}, {mime})::text AS "Value"
            """, ct);
        if (status != "delivered")
        {
            return status == "not_allowed" ? ErrNotAllowed : ErrInvalid;
        }

        await Audit(by, "service.delivered", id, new Dictionary<string, string?>(), ct);
        return null;
    }

    /// <summary>
    /// B-16.6: «ابعت الملف تاني»: the delivered file sent again (the first send fell outside the 24-hour window, or the
    /// client lost it). The worker sends it with a new key.
    /// </summary>
    public async Task<string?> ResendAsync(Guid by, Guid id, CancellationToken ct)
    {
        var status = await StatusAsync($"""SELECT hazem.resend_service_file({id})::text AS "Value" """, ct);
        if (status != "resent")
        {
            return status == "not_found" ? ErrNotFound : ErrNotAllowed;
        }

        await Audit(by, "service.resent", id, new Dictionary<string, string?>(), ct);
        return null;
    }

    /// <summary>B-16.7: the office closes it; money that came is due back, with a task (B-22).</summary>
    public async Task<string?> CloseAsync(Guid by, Guid id, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""SELECT hazem.close_service_order({id}, {by})::text AS "Value" """).SingleAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        switch (r.GetProperty("status").GetString())
        {
            case "closed":
                break;
            case "not_found":
                return ErrNotFound;
            default:
                return ErrNotAllowed;
        }

        if (r.GetProperty("refund").ValueKind == JsonValueKind.Number)
        {
            await store.AddTaskAsync(r.GetProperty("person").GetGuid(), $"service:{id:N}:refund", PendingTask.Refund,
                OfficeHours.DueAt(clock.GetUtcNow()), ct);
        }

        await Audit(by, "service.closed", id, new Dictionary<string, string?>(), ct);
        return null;
    }

    private async Task<string?> StatusAsync(FormattableString sql, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await db.Database.SqlQuery<string>(sql).SingleAsync(ct));
        return doc.RootElement.GetProperty("status").GetString();
    }

    private Task Audit(Guid by, string action, Guid id, Dictionary<string, string?> detail, CancellationToken ct) =>
        PanelAudit.WriteAsync(db, clock, by, action, "service", id, detail, ct);
}
