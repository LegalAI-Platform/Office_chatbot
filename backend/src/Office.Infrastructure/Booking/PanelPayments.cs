using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Panel;
using Office.Domain.Tasks;
using Office.Infrastructure.Panel;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Booking;

// TASK 09 (DECISIONS v1.13, B-05 / B-22 / S-04): the payments from the panel. Every role sees them and
// reviews a transfer (P1) or records a refund it made (P2); the system never does either. A receipt image
// is opened like a client's file in 07c (never cached, every opening in hazem.panel_audit). What the
// client must hear (partial, not received, refunded) is sent by the worker (BookingNotices).

public sealed record PaymentView(Guid AppointmentId, string Service, DateTimeOffset Start, string Appointment, string? Name, string WaId,
    string State, int AmountDue, int? AmountReceived, DateTimeOffset? ProofAt, int Receipts, bool Duplicate, string? ReviewedBy,
    DateTimeOffset? ReviewedAt, int? RefundDue, string? RefundReason, string? RefundedBy, DateTimeOffset? RefundedAt, string? RefundMethod);

public sealed class PanelPayments(HazemDbContext db, BookingStore store, TimeProvider clock)
{
    public const string ErrInvalid = "invalid";
    public const string ErrInvalidAmount = "invalid_amount";
    public const string ErrNotFound = "not_found";
    public const string ErrNotAllowed = "not_allowed";

    /// <summary>The «waiting for the review» filter first (the oldest receipt first), the refunds to make, or everything recent.</summary>
    public const string ShowReview = "review";
    public const string ShowRefunds = "refunds";
    public const string ShowAll = "all";

    public async Task<IReadOnlyList<PaymentView>?> ListAsync(string? show, CancellationToken ct)
    {
        if (show is not (null or ShowReview or ShowRefunds or ShowAll))
        {
            return null;
        }

        var since = clock.GetUtcNow().AddDays(-60).ToUniversalTime();
        var rows = from pay in db.Payments.AsNoTracking()
                   join a in db.Appointments.AsNoTracking() on pay.AppointmentId equals a.Id
                   join p in db.Persons.AsNoTracking() on a.PersonId equals p.Id
                   select new { pay, a, p };
        rows = show switch
        {
            ShowRefunds => rows.Where(x => x.pay.State == PaymentInfo.RefundRequested),
            ShowAll => rows.Where(x => x.a.StartsAt >= since && x.a.State != AppointmentInfo.Expired),
            _ => rows.Where(x => x.pay.State == PaymentInfo.ProofReceived || x.pay.State == PaymentInfo.DuplicateSuspected),
        };

        var list = await rows.OrderBy(x => x.pay.ProofAt ?? x.a.StartsAt).Take(300)
            .Select(x => new
            {
                x.pay,
                x.a.Service,
                x.a.StartsAt,
                AppointmentState = x.a.State,
                x.p.NameRaw,
                x.p.WaId,
                Receipts = db.PaymentReceipts.Count(r => r.PaymentId == x.pay.Id),
                Duplicate = db.PaymentReceipts.Any(r => r.PaymentId == x.pay.Id && r.DuplicateOf != null),
            }).ToListAsync(ct);
        var people = list.SelectMany(x => new[] { x.pay.ReviewedBy, x.pay.RefundedBy }).OfType<Guid>().Distinct().ToList();
        var names = await db.PanelUsers.AsNoTracking().Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        string? Who(Guid? id) => id is { } g && names.TryGetValue(g, out var n) ? n : null;
        return list.Select(x => new PaymentView(x.pay.AppointmentId, x.Service, x.StartsAt, x.AppointmentState, x.NameRaw, x.WaId, x.pay.State,
            x.pay.AmountDue, x.pay.AmountReceived, x.pay.ProofAt, x.Receipts, x.Duplicate, Who(x.pay.ReviewedBy), x.pay.ReviewedAt, x.pay.RefundDue,
            x.pay.RefundReason, Who(x.pay.RefundedBy), x.pay.RefundedAt, x.pay.RefundMethod)).ToList();
    }

    /// <summary>The n-th receipt (0 = the first) of an appointment's payment, for the panel to show.</summary>
    public async Task<ClientFileRef?> ReceiptAsync(Guid appointmentId, int index, CancellationToken ct)
    {
        if (index < 0)
        {
            return null;
        }

        var receipt = await (from r in db.PaymentReceipts.AsNoTracking()
                             join pay in db.Payments.AsNoTracking() on r.PaymentId equals pay.Id
                             where pay.AppointmentId == appointmentId
                             orderby r.ReceivedAt, r.Id
                             select r).Skip(index).FirstOrDefaultAsync(ct);
        return receipt is null ? null : new ClientFileRef("image", receipt.Bucket, receipt.Key, receipt.Mime, null);
    }

    /// <summary>Who saw a client's receipt: written once the file was really read from storage.</summary>
    public Task RecordOpenedAsync(Guid by, Guid appointmentId, int index, CancellationToken ct) =>
        PanelAudit.WriteAsync(db, clock, by, "payment.receipt_open", "payment", appointmentId,
            new Dictionary<string, string?> { ["index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct);

    /// <summary>
    /// B-05: verified (with the amount that came: more than due opens a refund of the difference, P4),
    /// partial (with the amount) or not_received. The client hears partial and not received from the worker.
    /// </summary>
    public async Task<string?> ReviewAsync(Guid by, Guid appointmentId, string? state, int? received, CancellationToken ct)
    {
        if (state is not (PaymentInfo.Verified or PaymentInfo.Partial or PaymentInfo.NotReceived) || received is < 0 or > 1_000_000)
        {
            return ErrInvalid;
        }

        var json = await db.Database.SqlQuery<string>($"""
            SELECT hazem.mark_payment({appointmentId}, {state}, {received}, {by})::text AS "Value"
            """).SingleAsync(ct);
        using var doc = JsonDocument.Parse(json);
        switch (doc.RootElement.GetProperty("status").GetString())
        {
            case "marked":
                break;
            case "invalid_amount":
                return ErrInvalidAmount;
            case "not_found":
                return ErrNotFound;
            case "invalid":
                return ErrInvalid;
            default:
                return ErrNotAllowed;
        }

        await PanelAudit.WriteAsync(db, clock, by, "payment." + state, "payment", appointmentId, new Dictionary<string, string?>(), ct);
        if (doc.RootElement.GetProperty("refund").GetBoolean() && await PersonOfAsync(appointmentId, ct) is { } person)
        {
            await store.AddTaskAsync(person, $"payment:{appointmentId:N}:overpaid", PendingTask.Refund, OfficeHours.DueAt(clock.GetUtcNow()), ct);
        }

        return null;
    }

    /// <summary>B-22 (P2): the person who transferred the money back records it; then (only then) the client is told.</summary>
    public async Task<string?> RefundedAsync(Guid by, Guid appointmentId, string? method, string? note, CancellationToken ct)
    {
        method = method?.Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (string.IsNullOrEmpty(method) || method.Length > 100 || note is { Length: > 200 })
        {
            return ErrInvalid;
        }

        var json = await db.Database.SqlQuery<string>($"""
            SELECT hazem.mark_refunded({appointmentId}, {by}, {method}, {note})::text AS "Value"
            """).SingleAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status").GetString();
        if (status != "refunded")
        {
            return status == "not_found" ? ErrNotFound : ErrNotAllowed;
        }

        await PanelAudit.WriteAsync(db, clock, by, "payment.refunded", "payment", appointmentId, new Dictionary<string, string?>(), ct);
        return null;
    }

    private async Task<Guid?> PersonOfAsync(Guid appointmentId, CancellationToken ct) =>
        await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => (Guid?)a.PersonId).FirstOrDefaultAsync(ct);
}
