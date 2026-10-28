using Microsoft.EntityFrameworkCore;
using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Urf;
using Office.Domain.Tasks;
using Office.Infrastructure.Booking;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Urf;

/// <summary>
/// TASK 11 (v1.15, B-11): what the client hears after the lawyer's review, from the worker (never inside the panel's
/// request): the confirmation with who must come (B-11.7), or the refusal (the appointment cancelled, the money back).
/// Also the office's task when the appointment is within 24 hours and no document came. A send that fails becomes a
/// task (booking_message_failed).
/// </summary>
public sealed class UrfNotices(HazemDbContext db, BookingStore store, IApprovedTexts texts, NoticeSender notices, TimeProvider clock)
{
    public const int Batch = 20;
    private const string Requester = "panel-urf";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.Database.SqlQuery<string>($"""SELECT hazem.urf_due()::text AS "Value" """).SingleAsync(ct);
        foreach (var c in System.Text.Json.JsonDocument.Parse(due).RootElement.EnumerateArray())
        {
            await store.AddTaskAsync(c.GetProperty("person").GetGuid(), $"urf:{c.GetProperty("id").GetGuid():N}:documents",
                PendingTask.UrfDocumentsMissing, OfficeHours.DueAt(now), ct);
        }

        var cases = await (from c in db.UrfCases.AsNoTracking()
                           join p in db.Persons.AsNoTracking() on c.PersonId equals p.Id
                           join a in db.Appointments.AsNoTracking() on c.AppointmentId equals (Guid?)a.Id into apps
                           from a in apps.DefaultIfEmpty()
                           where c.ClientNotice == "pending"
                           orderby c.UpdatedAt
                           select new { c, p.WaId, Start = a == null ? (DateTimeOffset?)null : a.StartsAt }).Take(Batch).ToListAsync(ct);
        if (cases.Count == 0)
        {
            return 0;
        }

        var active = await texts.GetActiveAsync(ct);
        foreach (var x in cases)
        {
            var urf = UrfStore.ToCase(x.c);
            var step = x.c.NoticeKind == "approved" && x.Start is { } start
                ? new ReplyStep(TextKeys.UrfConfirmed, null, new Dictionary<string, string>
                {
                    ["day"] = BookingWords.Day(start, now),
                    ["time"] = BookingWords.Time(start),
                    ["attendees"] = urf.Attendees(),
                })
                : new ReplyStep(TextKeys.UrfRejected);
            var id = $"urf:{x.c.Id:N}:{x.c.NoticeKind}";
            var told = await notices.SendAsync(x.WaId, id, Requester, step, active, ct);
            if (!told)
            {
                await store.AddTaskAsync(x.c.PersonId, id, PendingTask.BookingMessageFailed, OfficeHours.DueAt(now), ct);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.urf_cases SET client_notice = {(told ? "sent" : "failed")} WHERE id = {x.c.Id} AND client_notice = 'pending' AND notice_kind IS NOT DISTINCT FROM {x.c.NoticeKind}", ct); // v1.28
        }

        return cases.Count;
    }
}
