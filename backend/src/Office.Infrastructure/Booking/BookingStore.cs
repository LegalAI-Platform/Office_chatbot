using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Application.Booking;
using Office.Domain.Booking;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Booking;

/// <summary>
/// hazem.booking_* and hazem.appointments (migration 0013). Booking and moving go through the SQL
/// functions (one lock, the buffer, no two confirmed appointments at once); the rest is read here.
/// </summary>
public sealed class BookingStore(HazemDbContext db) : IBookingStore
{
    public async Task<Schedule> LoadScheduleAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var weekly = await db.BookingWeeklyHours.AsNoTracking().ToListAsync(ct);
        var exceptions = await db.BookingExceptions.AsNoTracking().Where(e => e.Day >= from && e.Day <= to).ToListAsync(ct);
        return new Schedule(Weekly(weekly), exceptions.Select(ToException).ToList());
    }

    /// <summary>The rows are checked when saved (the panel); here they are only read.</summary>
    internal static WeeklyHours Weekly(IEnumerable<BookingWeeklyHourRow> rows) =>
        WeeklyHours.Create(rows.Select(r => ((DayOfWeek)r.Weekday, new MinuteRange(r.StartMinute, r.EndMinute)))).Hours
        ?? WeeklyHours.Create([]).Hours!;

    internal static DayException ToException(BookingExceptionRow e) => new(
        e.Day,
        e.Kind switch
        {
            "closed_day" => ExceptionKind.ClosedDay,
            "closed_hours" => ExceptionKind.ClosedHours,
            _ => ExceptionKind.CustomHours,
        },
        e.StartMinute is { } s && e.EndMinute is { } end ? new MinuteRange(s, end) : null);

    public async Task<IReadOnlyList<AppointmentInfo>> TakenBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var (f, t) = (from.ToUniversalTime(), to.ToUniversalTime());
        return (await db.Appointments.AsNoTracking()
            .Where(a => (a.State == AppointmentInfo.Confirmed || a.State == AppointmentInfo.PendingPayment) && a.StartsAt < t && a.EndsAt > f)
            .OrderBy(a => a.StartsAt).ToListAsync(ct)).Select(ToInfo).ToList();
    }

    public async Task<AppointmentInfo?> UpcomingForAsync(Guid personId, DateTimeOffset now, CancellationToken ct)
    {
        var n = now.ToUniversalTime();
        var row = await db.Appointments.AsNoTracking()
            .Where(a => a.PersonId == personId && (a.State == AppointmentInfo.Confirmed || a.State == AppointmentInfo.PendingPayment) && a.EndsAt > n)
            .OrderBy(a => a.StartsAt).FirstOrDefaultAsync(ct);
        return row is null ? null : ToInfo(row);
    }

    /// <summary>hazem.file_target (an image): the one rule attach_receipt uses too (the database's clock).</summary>
    public async Task<AppointmentInfo?> ReceiptTargetAsync(Guid personId, DateTimeOffset now, CancellationToken ct) =>
        await db.Database.SqlQuery<Guid?>($"""
            SELECT t.id AS "Value" FROM hazem.file_target({personId}, true) t WHERE t.kind IN ('hold', 'booking_payment', 'late_hold')
            """).SingleOrDefaultAsync(ct) is { } id
            ? await FindAsync(id, ct)
            : null;

    public async Task<ReceiptOutcome> AttachReceiptAsync(ReceiptRequest r, bool lateFree, int bufferMinutes, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT hazem.attach_receipt({r.PersonId}, {r.Wamid}, {r.Bucket}, {r.Key}, {r.Sha256}, {r.MimeType}, {lateFree}, {bufferMinutes})::text AS "Value"
            """).SingleAsync(ct);
        using var doc = JsonDocument.Parse(json);
        Guid? id = doc.RootElement.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetGuid() : null;
        Guid? order = doc.RootElement.TryGetProperty("order", out var o) && o.ValueKind == JsonValueKind.String ? o.GetGuid() : null;
        return new ReceiptOutcome(doc.RootElement.GetProperty("status").GetString() switch
        {
            "confirmed" => ReceiptStatus.Confirmed,
            "confirmed_late" => ReceiptStatus.ConfirmedLate,
            "late_taken" => ReceiptStatus.LateTaken,
            "received" => ReceiptStatus.Received,
            "order_received" => ReceiptStatus.OrderReceived,
            "exists" => ReceiptStatus.Exists,
            _ => ReceiptStatus.None,
        }, id, order, doc.RootElement.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null);
    }

    public async Task<PaymentInfo?> PaymentAsync(Guid appointmentId, CancellationToken ct) =>
        await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.AppointmentId == appointmentId, ct) is { } p
            ? new PaymentInfo(p.State, p.AmountDue, p.AmountReceived, p.ProofAt)
            : null;

    public async Task<int?> RequestRefundAsync(Guid appointmentId, string reason, CancellationToken ct) =>
        await db.Database.SqlQuery<int?>($"""SELECT hazem.request_refund({appointmentId}, {reason}) AS "Value" """).SingleAsync(ct);

    public async Task<AppointmentInfo?> FindAsync(Guid id, CancellationToken ct) =>
        await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) is { } row ? ToInfo(row) : null;

    public async Task<BookOutcome> BookAsync(BookRequest r, CancellationToken ct)
    {
        var start = r.Start.ToUniversalTime();
        var json = await db.Database.SqlQuery<string>($"""
            SELECT hazem.book_appointment({r.Id}, {r.PersonId}, {r.IntakeId}, {r.Service.Code}, {start}, {start.AddMinutes(r.Service.Minutes)},
                                          {r.Service.Amount}, {r.TurnId}, {r.BufferMinutes}, {r.MatterType}, {r.HoldUntil.ToUniversalTime()})::text AS "Value"
            """).SingleAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status").GetString();
        Guid? id = doc.RootElement.TryGetProperty("id", out var i) ? i.GetGuid() : null;
        if (r.CallApp is { } app && status is "held" or "booked_paid")
        {
            // 1.25 (B-23): where the lawyer calls, on the new row only (an existing booking keeps its own).
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.appointments SET call_app = {app} WHERE id = {r.Id}", ct);
        }

        return new BookOutcome(status switch
        {
            "held" => BookStatus.Held,
            "booked_paid" => BookStatus.BookedPaid,
            "exists" => BookStatus.Exists,
            "already_booked" => BookStatus.AlreadyBooked,
            "has_hold" => BookStatus.HasHold,
            _ => BookStatus.Taken,
        }, id);
    }

    public async Task<MoveStatus> MoveAsync(Guid id, DateTimeOffset start, DateTimeOffset end, string turnId, int bufferMinutes, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT hazem.move_appointment({id}, {start.ToUniversalTime()}, {end.ToUniversalTime()}, {turnId}, {bufferMinutes}, 1)::text AS "Value"
            """).SingleAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("status").GetString() switch
        {
            "moved" => MoveStatus.Moved,
            "exists" => MoveStatus.Exists,
            "taken" => MoveStatus.Taken,
            "not_found" => MoveStatus.NotFound,
            _ => MoveStatus.NotAllowed,
        };
    }

    public async Task<bool> CancelAsync(Guid id, string state, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.appointments
               SET state = {state},
                   calendar_state = CASE WHEN calendar_event_id IS NULL THEN 'removed' ELSE 'remove' END,
                   calendar_attempts = 0, calendar_next_at = now(),
                   client_notice = CASE WHEN notice_kind = 'hold_reminder' THEN NULL ELSE client_notice END
             WHERE id = {id} AND state IN ('confirmed', 'pending_payment')
            """, ct) == 1;

    /// <summary>TASK 12: «هحضر ✅» for this time; the calendar event is updated to show it.</summary>
    public async Task<bool> ConfirmAttendanceAsync(Guid id, DateTimeOffset start, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.appointments
               SET attendance_at = now(), attendance_for = starts_at,
                   calendar_attempts = CASE WHEN calendar_state IN ('synced', 'update') THEN 0 ELSE calendar_attempts END,
                   calendar_next_at = CASE WHEN calendar_state IN ('synced', 'update') THEN now() ELSE calendar_next_at END,
                   calendar_state = CASE WHEN calendar_state = 'synced' THEN 'update' ELSE calendar_state END
             WHERE id = {id} AND state = 'confirmed' AND starts_at = {start.ToUniversalTime()}
               AND attendance_for IS DISTINCT FROM starts_at
            """, ct);
        var at = start.ToUniversalTime();
        return await db.Appointments.AnyAsync(a => a.Id == id && a.State == "confirmed" && a.AttendanceFor == at, ct);
    }

    /// <summary>A task the booking opens outside a turn (the panel, the calendar worker). Once per key.</summary>
    public Task AddTaskAsync(Guid personId, string key, string type, DateTimeOffset dueAt, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.staff_tasks (person_id, turn_id, type, due_at)
            VALUES ({personId}, {key}, {type}, {dueAt.ToUniversalTime()})
            ON CONFLICT (turn_id, type) DO NOTHING
            """, ct);

    internal static AppointmentInfo ToInfo(AppointmentRow a) =>
        new(a.Id, a.PersonId, a.Service, a.StartsAt, a.EndsAt, a.State, a.Reschedules, a.Amount, a.MatterType, a.HoldUntil, a.CallApp);
}
