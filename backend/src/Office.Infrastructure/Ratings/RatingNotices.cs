using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Office.Application.Ports;
using Office.Application.Ratings;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.UseCases;
using Office.Infrastructure.Booking;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Ratings;

/// <summary>
/// v1.31 (DECISIONS B-26 / M-03): the service rating, from the worker (with the reminders, every 10 seconds).
/// <list type="bullet">
/// <item>Every round makes a rating for every service finished after rating.since that has none: an appointment marked
/// completed (a consultation or an Urfi contract; never an urgent one or an Urfi divorce), due 2 hours later; a written
/// service delivered, due 24 hours later (<see cref="RatingRules"/>).</item>
/// <item>A rating whose time came goes out once, unless the conversation is muted, the person was asked in the last 30
/// days, or ratings are off: inside the 24-hour window a WhatsApp list («قيّم الخدمة»); outside it the M-03 template
/// service_rating with the same five answers as quick replies. A template Meta has not approved (yet) → skipped
/// (no_template), never a task: a rating is not worth a phone call.</item>
/// </list>
/// </summary>
public sealed class RatingNotices(HazemDbContext db, ISettingsReader settingsReader, IApprovedTexts texts, SendOutbound send, TimeProvider clock)
{
    public const int Batch = 20;
    private const string Requester = "ratings";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUniversalTime(); // the database takes UTC only
        var settings = RatingSettings.From(await settingsReader.GetAsync(ct));
        await MakeAsync(settings, ct);
        return await SendDueAsync(settings, now, ct);
    }

    /// <summary>A rating's own key (its id: one rating per appointment or order, never reused).</summary>
    public static string Key(Guid rating) => $"rating:{rating:N}";

    /// <summary>A rating for every finished service that has none (only those finished after rating.since).</summary>
    public async Task MakeAsync(RatingSettings s, CancellationToken ct)
    {
        // A send cut off half way (the API stopped): tried again; the same key makes n8n answer «already sent» if it went.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE hazem.service_ratings SET state = 'scheduled'
             WHERE state = 'sending' AND updated_at < now() - interval '5 minutes'
            """, ct);
        if (s.Since is not { } since)
        {
            return; // no starting time: nothing is old enough to be sure about
        }

        var services = RatingRules.AppointmentServices.ToList();
        var done = await db.Appointments.AsNoTracking()
            .Where(a => a.State == "completed" && a.CompletedAt != null && a.CompletedAt >= since && services.Contains(a.Service)
                        && !db.ServiceRatings.Any(r => r.AppointmentId == a.Id))
            .OrderBy(a => a.CompletedAt)
            .Select(a => new { a.Id, a.PersonId, a.Service, At = a.CompletedAt!.Value }).Take(100).ToListAsync(ct);
        foreach (var a in done)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.service_ratings (person_id, appointment_id, service, finished_at, due_at)
                VALUES ({a.PersonId}, {a.Id}, {a.Service}, {a.At.ToUniversalTime()}, {RatingRules.Due(a.Service, a.At, s).ToUniversalTime()})
                ON CONFLICT DO NOTHING
                """, ct);
        }

        var kinds = RatingRules.OrderServices.ToList();
        var delivered = await db.ServiceOrders.AsNoTracking()
            .Where(o => (o.State == "delivered" || o.State == "closed") && o.DeliveredAt != null && o.DeliveredAt >= since
                        && kinds.Contains(o.Kind) && !db.ServiceRatings.Any(r => r.ServiceOrderId == o.Id))
            .OrderBy(o => o.DeliveredAt)
            .Select(o => new { o.Id, o.PersonId, o.Kind, At = o.DeliveredAt!.Value }).Take(100).ToListAsync(ct);
        foreach (var o in delivered)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO hazem.service_ratings (person_id, service_order_id, service, finished_at, due_at)
                VALUES ({o.PersonId}, {o.Id}, {o.Kind}, {o.At.ToUniversalTime()}, {RatingRules.Due(o.Kind, o.At, s).ToUniversalTime()})
                ON CONFLICT DO NOTHING
                """, ct);
        }
    }

    private async Task<int> SendDueAsync(RatingSettings s, DateTimeOffset now, CancellationToken ct)
    {
        var due = await (from r in db.ServiceRatings.AsNoTracking()
                         join p in db.Persons.AsNoTracking() on r.PersonId equals p.Id
                         where r.State == "scheduled" && r.DueAt <= now
                         orderby r.DueAt
                         select new
                         {
                             r.Id, r.PersonId, r.Service, r.DueAt, p.WaId,
                             Muted = db.Conversations.Any(c => c.PersonId == r.PersonId && c.State == "muted"),
                             LastAsked = db.ServiceRatings.Where(o => o.PersonId == r.PersonId && o.State == "sent").Max(o => o.SentAt),
                         }).Take(Batch).ToListAsync(ct);
        if (due.Count == 0)
        {
            return 0;
        }

        var active = await texts.GetActiveAsync(ct);
        var on = RatingTexts.Keys.All(active.ContainsKey);
        var asked = new HashSet<Guid>(); // two services finished together: the second is «recent» too
        foreach (var r in due)
        {
            if (RatingRules.Skip(s, on, r.Muted, asked.Contains(r.PersonId) ? now : r.LastAsked, r.DueAt, now) is { } why)
            {
                await SkipAsync(r.Id, why, null, ct);
                continue;
            }

            var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.service_ratings SET state = 'sending' WHERE id = {r.Id} AND state = 'scheduled'", ct);
            if (claimed == 0)
            {
                continue;
            }

            var (channel, error, noTemplate) = await SendAsync(r.Id, r.WaId, r.Service, active, ct);
            if (channel is not null)
            {
                asked.Add(r.PersonId);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE hazem.service_ratings SET state = 'sent', channel = {channel}, sent_at = {now}
                     WHERE id = {r.Id} AND state = 'sending'
                    """, ct);
            }
            else if (noTemplate)
            {
                await SkipAsync(r.Id, RatingSkips.NoTemplate, error, ct, from: "sending");
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE hazem.service_ratings SET state = 'failed', error_code = {Code(error)}
                     WHERE id = {r.Id} AND state = 'sending'
                    """, ct);
            }
        }

        return due.Count;
    }

    /// <summary>
    /// The list (inside the 24-hour window), else the template; the channel it went by, or why it did not
    /// (<c>NoTemplate</c>: the window was closed and the template was refused).
    /// </summary>
    private async Task<(string? Channel, string? Error, bool NoTemplate)> SendAsync(Guid id, string waId, string service,
        IReadOnlyDictionary<string, ApprovedText> active, CancellationToken ct)
    {
        var name = RatingRules.ServiceName(service);
        var key = Key(id);
        var rows = RatingTexts.Rows.Select(x => new ReplyChoice(RatingButtons.Id(id, x.Score), x.Key)).ToList();
        var composed = ReplyComposer.Compose(key, null,
            [new ReplyStep(TextKeys.RatingAsk, null, new Dictionary<string, string> { ["service"] = name },
                new ReplyChoices(InteractivePayload.List, rows, TextKeys.BtnRating))], active);
        if (composed.Actions is not [{ Type: ActionType.SendInteractive } list])
        {
            return (null, "list", false); // never the question without its answers
        }

        var result = await send.SendAsync(new OutboundMessage(list.Type, waId, list.IdempotencyKey, Requester, list.Text,
            Interactive: list.Interactive), ct);
        if (result.Ok)
        {
            return ("message", null, false);
        }

        if (result.ErrorCode != ReminderNotices.OutsideWindow)
        {
            return (null, result.ErrorCode ?? "send_failed", false);
        }

        // M-03: outside the 24-hour window only an approved template goes out; the same question, the same five answers.
        var components = new JsonArray(new JsonObject
        {
            ["type"] = "body",
            ["parameters"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = name }),
        });
        for (var i = 0; i < RatingTexts.Rows.Count; i++)
        {
            components.Add(new JsonObject
            {
                ["type"] = "button",
                ["sub_type"] = "quick_reply",
                ["index"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["parameters"] = new JsonArray(new JsonObject { ["type"] = "payload", ["payload"] = RatingButtons.Id(id, RatingTexts.Rows[i].Score) }),
            });
        }

        var template = await send.SendAsync(new OutboundMessage(ActionType.SendTemplate, waId, $"{key}:template", Requester,
            TemplateName: RatingTexts.Template, Language: "ar", Components: components), ct);
        return template.Ok ? ("template", null, false) : (null, template.ErrorCode ?? "send_failed", true);
    }

    private Task SkipAsync(Guid id, string why, string? error, CancellationToken ct, string from = "scheduled") =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.service_ratings SET state = 'skipped', skip_reason = {why}, error_code = {(error is null ? null : Code(error))}
             WHERE id = {id} AND state = {from}
            """, ct);

    /// <summary>The code only (never a message from Meta or n8n), within the column's limit.</summary>
    private static string Code(string? error) => error is null ? "unknown" : error.Length <= 80 ? error : error[..80];
}
