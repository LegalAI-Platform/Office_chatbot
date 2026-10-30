using Microsoft.EntityFrameworkCore;
using Office.Application.Conversations;
using Office.Application.Ratings;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Ratings;

/// <summary>
/// v1.31: what the conversation reads and writes of hazem.service_ratings (B-26), and the person's requests for the
/// returning-client rule (B-01.7). Nothing here deletes anything.
/// </summary>
public sealed class RatingStore(HazemDbContext db) : IRatingStore, IIntakeHistory
{
    /// <summary>B-26: the words after «يحتاج للتحسين» are its reason only within this time.</summary>
    public static TimeSpan ReasonWithin { get; } = TimeSpan.FromHours(24);

    public async Task<RatingAnswer> AnswerAsync(Guid personId, RatingPress press, string turnId, DateTimeOffset now, CancellationToken ct)
    {
        short? score = press.Score is { } s ? (short)s : null;
        var hasScore = score is not null;
        // The first answer; after «ليس الآن» a score still counts (the client rated later after all).
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.service_ratings SET score = {score}, answered_at = {now.ToUniversalTime()}, answered_turn_id = {turnId}
             WHERE id = {press.RatingId} AND person_id = {personId} AND state = 'sent'
               AND (answered_at IS NULL OR (score IS NULL AND {hasScore}))
            """, ct);
        if (changed > 0)
        {
            return new RatingAnswer(RatingAnswerStatus.Recorded, press.Score);
        }

        var row = await db.ServiceRatings.AsNoTracking()
            .Where(r => r.Id == press.RatingId && r.PersonId == personId && r.State == "sent" && r.AnsweredAt != null)
            .Select(r => new { r.Score }).FirstOrDefaultAsync(ct);
        return row is null ? new RatingAnswer(RatingAnswerStatus.Unknown, null) : new RatingAnswer(RatingAnswerStatus.Again, row.Score);
    }

    public async Task<bool> TakeReasonAsync(Guid personId, string waId, string turnId, DateTimeOffset now, CancellationToken ct)
    {
        // Only his very next message (the press is his last turn), within 24 hours: the task takes that turn, so the panel
        // shows his words as the task's «كلام العميل».
        var taken = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH r AS (
              UPDATE hazem.service_ratings s SET reason_at = {now.ToUniversalTime()}
               WHERE s.id = (SELECT x.id FROM hazem.service_ratings x
                              WHERE x.person_id = {personId} AND x.score = 2 AND x.reason_at IS NULL
                                AND x.answered_at >= {(now - ReasonWithin).ToUniversalTime()}
                                AND x.answered_turn_id = (SELECT t.turn_id FROM hazem.turns t WHERE t.wa_id = {waId}
                                                           ORDER BY t.created_at DESC, t.id DESC LIMIT 1)
                              ORDER BY x.answered_at DESC LIMIT 1)
              RETURNING s.person_id, s.answered_turn_id)
            UPDATE hazem.staff_tasks t SET turn_id = {turnId}
              FROM r
             WHERE t.person_id = r.person_id AND t.type = 'rating_low' AND t.turn_id = r.answered_turn_id
            """, ct);
        return taken > 0;
    }

    public async Task<IntakeFacts> FactsAsync(Guid personId, string waId, Guid intakeId, CancellationToken ct)
    {
        // Silent since his last stored turn, by the database's clock (it stamped the turns; the API's clock may differ).
        var seconds = await db.Database.SqlQuery<double?>($"""
            SELECT extract(epoch FROM now() - max(created_at))::float8 AS "Value" FROM hazem.turns WHERE wa_id = {waId}
            """).SingleAsync(ct);
        var last = seconds is { } s ? TimeSpan.FromSeconds(s) : (TimeSpan?)null;
        var items = new List<RequestItem>();
        items.AddRange(await db.Appointments.AsNoTracking().Where(a => a.PersonId == personId)
            .Select(a => new RequestItem(RequestItem.Appointment, a.State, a.IntakeId == intakeId, a.StartsAt)).ToListAsync(ct));
        items.AddRange(await db.ServiceOrders.AsNoTracking().Where(o => o.PersonId == personId)
            .Select(o => new RequestItem(RequestItem.Order, o.State, o.IntakeId == intakeId, null)).ToListAsync(ct));
        items.AddRange(await db.UrfCases.AsNoTracking().Where(c => c.PersonId == personId)
            .Select(c => new RequestItem(RequestItem.Urf, c.State, c.IntakeId == intakeId, null)).ToListAsync(ct));
        items.AddRange(await db.Payments.AsNoTracking()
            .Where(p => db.Appointments.Any(a => a.Id == p.AppointmentId && a.PersonId == personId)
                        || db.ServiceOrders.Any(o => o.Id == p.ServiceOrderId && o.PersonId == personId))
            .Select(p => new RequestItem(RequestItem.Payment, p.State, false, null)).ToListAsync(ct));
        return new IntakeFacts(last, items);
    }
}
