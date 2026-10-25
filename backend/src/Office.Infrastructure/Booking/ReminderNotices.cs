using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Ports;
using Office.Application.Reminders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.UseCases;
using Office.Application.Urf;
using Office.Domain.Tasks;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Booking;

/// <summary>
/// TASK 12 (DECISIONS v1.16, B-18 / M-03): the reminders before a confirmed appointment, from the worker.
/// <list type="bullet">
/// <item>Every round makes the reminders of the confirmed appointments that have none for their time (B-18's timing,
/// <see cref="ReminderRules"/>) and cancels the ones of an appointment that moved, was cancelled or ended.</item>
/// <item>A reminder whose time came goes out once: inside the 24-hour window a normal message (the day before with
/// «هحضر ✅» and «تغيير أو إلغاء»); outside it the M-03 template with the same words and buttons. A refusal (the
/// template not approved yet, the office's address not written) → a task (reminder_failed): the office calls.</item>
/// </list>
/// </summary>
public sealed class ReminderNotices(HazemDbContext db, BookingStore store, ISettingsReader settingsReader, IApprovedTexts texts,
    SendOutbound send, TimeProvider clock)
{
    public const int Batch = 20;
    public const string OutsideWindow = "OUTSIDE_24H_WINDOW";
    private const string Requester = "reminders";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUniversalTime(); // the database takes UTC only
        await ReconcileAsync(now, ct);
        return await SendDueAsync(now, ct);
    }

    /// <summary>A reminder's own key: its appointment, kind and time (never the row id, which a rollback reuses).</summary>
    public static string Key(Guid appointment, string kind, DateTimeOffset start) => $"reminder:{appointment:N}:{kind}:{start.ToUnixTimeSeconds()}";

    /// <summary>The reminders follow the appointments: made for a confirmed time, cancelled for any other.</summary>
    public async Task ReconcileAsync(DateTimeOffset now, CancellationToken ct)
    {
        // A send cut off half way (the API stopped): tried again; the same key makes n8n answer «already sent» if it went.
        // The database's own clock on both sides (updated_at is set by its trigger).
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE hazem.appointment_reminders SET state = 'scheduled'
             WHERE state = 'sending' AND updated_at < now() - interval '5 minutes'
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.appointment_reminders r SET state = 'cancelled'
              FROM hazem.appointments a
             WHERE a.id = r.appointment_id AND r.state = 'scheduled'
               AND (a.state <> 'confirmed' OR a.starts_at <> r.starts_at)
            """, ct);

        var missing = await db.Appointments.AsNoTracking()
            .Where(a => a.State == AppointmentInfo.Confirmed && a.StartsAt > now
                        && db.AppointmentReminders.Count(r => r.AppointmentId == a.Id && r.StartsAt == a.StartsAt) < ReminderKinds.All.Count)
            .OrderBy(a => a.StartsAt)
            .Select(a => new { a.Id, a.StartsAt }).Take(100).ToListAsync(ct);
        foreach (var a in missing)
        {
            foreach (var kind in ReminderKinds.All)
            {
                var plan = ReminderRules.Plan(kind, a.StartsAt, now);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO hazem.appointment_reminders (appointment_id, kind, starts_at, due_at, state, skip_reason)
                    VALUES ({a.Id}, {kind}, {a.StartsAt.ToUniversalTime()}, {plan.Due?.ToUniversalTime()},
                            {(plan.Skip is null ? "scheduled" : "skipped")}, {plan.Skip})
                    ON CONFLICT (appointment_id, kind, starts_at) DO NOTHING
                    """, ct);
            }
        }
    }

    private async Task<int> SendDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await (from r in db.AppointmentReminders.AsNoTracking()
                         join a in db.Appointments.AsNoTracking() on r.AppointmentId equals a.Id
                         join p in db.Persons.AsNoTracking() on a.PersonId equals p.Id
                         where r.State == "scheduled" && r.DueAt <= now && a.State == AppointmentInfo.Confirmed && a.StartsAt == r.StartsAt
                         orderby r.DueAt
                         select new { r.Id, r.Kind, a.PersonId, AppointmentId = a.Id, a.Service, a.StartsAt, p.WaId }).Take(Batch).ToListAsync(ct);
        if (due.Count == 0)
        {
            return 0;
        }

        var active = await texts.GetActiveAsync(ct);
        var on = ReminderTexts.Keys.All(active.ContainsKey);
        var office = BookingSettings.From(await settingsReader.GetAsync(ct)).Office;
        foreach (var r in due)
        {
            if (!on || !ReminderRules.StillUseful(r.Kind, r.StartsAt, now))
            {
                await SkipAsync(r.Id, on ? ReminderSkips.Late : ReminderSkips.Off, ct);
                continue;
            }

            var service = BookingService.ByCode(r.Service) ?? BookingService.PhoneVideo;
            var attendees = service.IsUrf
                ? await db.UrfCases.AsNoTracking().Where(c => c.AppointmentId == r.AppointmentId)
                    .Select(c => UrfCase.AttendeesFor(c.Kind, c.Witnesses)).FirstOrDefaultAsync(ct)
                : null;
            var (detailsKey, detailsFill) = ReminderTexts.Details(service, office, attendees);
            var details = active.TryGetValue(detailsKey, out var d) ? TextRenderer.Render(d, detailsFill).Text : null;
            var key = Key(r.AppointmentId, r.Kind, r.StartsAt);

            // Claimed just before it goes, and only while its appointment is still confirmed at that time: one that was
            // cancelled or moved a moment ago, or that another round took, is left alone.
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE hazem.appointment_reminders r SET state = 'sending'
                 WHERE r.id = {r.Id} AND r.state = 'scheduled'
                   AND EXISTS (SELECT 1 FROM hazem.appointments a WHERE a.id = r.appointment_id AND a.state = 'confirmed' AND a.starts_at = r.starts_at)
                """, ct);
            if (claimed == 0)
            {
                continue;
            }

            var (channel, error) = details is null
                ? (null, "details_missing") // the office's address or map not written yet (P1): never half a reminder
                : await SendAsync(key, r.Kind, r.WaId, r.AppointmentId, r.StartsAt, service, details, active, ct);
            if (channel is not null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE hazem.appointment_reminders SET state = 'sent', channel = {channel}, sent_at = {now.ToUniversalTime()}
                     WHERE id = {r.Id} AND state = 'sending'
                    """, ct);
                continue;
            }

            await store.AddTaskAsync(r.PersonId, key, PendingTask.ReminderFailed, OfficeHours.DueAt(now), ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE hazem.appointment_reminders SET state = 'failed', error_code = {Code(error)}
                 WHERE id = {r.Id} AND state = 'sending'
                """, ct);
        }

        return due.Count;
    }

    /// <summary>The message (inside the 24-hour window), else the template; the channel it went by, or why it did not.</summary>
    private async Task<(string? Channel, string? Error)> SendAsync(string key, string kind, string waId, Guid appointment, DateTimeOffset start,
        BookingService service, string details, IReadOnlyDictionary<string, ApprovedText> active, CancellationToken ct)
    {
        var fill = new Dictionary<string, string>
        {
            ["day"] = BookingWords.Date(start),
            ["time"] = BookingWords.Time(start),
            ["service"] = service.Name,
            ["details"] = details,
        };
        var dayBefore = kind == ReminderKinds.DayBefore;
        var choices = dayBefore
            ? new ReplyChoices(InteractivePayload.Buttons,
            [
                new ReplyChoice(ReminderButtons.Attend(appointment, start), TextKeys.BtnReminderAttend),
                new ReplyChoice(ReminderButtons.Change(appointment), TextKeys.BtnReminderChange),
            ])
            : null;
        var composed = ReplyComposer.Compose(key, null, [new ReplyStep(ReminderTexts.TextKey(kind), null, fill, choices)], active);
        if (composed.Actions.Count == 0)
        {
            return (null, "text");
        }

        string? error = null;
        var sent = 0;
        foreach (var action in composed.Actions)
        {
            var result = await send.SendAsync(new OutboundMessage(action.Type, waId, action.IdempotencyKey, Requester, action.Text,
                Interactive: action.Interactive), ct);
            if (!result.Ok)
            {
                error = result.ErrorCode ?? "send_failed";
                break;
            }

            sent++;
        }

        if (error is null)
        {
            return ("message", null);
        }

        if (error != OutsideWindow || sent > 0)
        {
            return (null, error);
        }

        // M-03: outside the 24-hour window only an approved template goes out; the same words, the same buttons.
        var body = new JsonArray(fill["day"], fill["time"], fill["service"], details);
        if (!dayBefore)
        {
            body.RemoveAt(0); // reminder_2h_before: {{1}} time, {{2}} the kind, {{3}} the details
        }

        var components = new JsonArray(new JsonObject
        {
            ["type"] = "body",
            ["parameters"] = new JsonArray(body.Select(v => (JsonNode?)new JsonObject { ["type"] = "text", ["text"] = (string?)v }).ToArray()),
        });
        if (dayBefore)
        {
            components.Add(Button(0, ReminderButtons.Attend(appointment, start)));
            components.Add(Button(1, ReminderButtons.Change(appointment)));
        }

        var template = await send.SendAsync(new OutboundMessage(ActionType.SendTemplate, waId, $"{key}:template", Requester,
            TemplateName: ReminderTexts.Template(kind), Language: "ar", Components: components), ct);
        return template.Ok ? ("template", null) : (null, template.ErrorCode ?? "send_failed");
    }

    private static JsonObject Button(int index, string payload) => new()
    {
        ["type"] = "button",
        ["sub_type"] = "quick_reply",
        ["index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["parameters"] = new JsonArray(new JsonObject { ["type"] = "payload", ["payload"] = payload }),
    };

    private Task SkipAsync(long id, string why, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE hazem.appointment_reminders SET state = 'skipped', skip_reason = {why} WHERE id = {id} AND state = 'scheduled'
        """, ct);

    /// <summary>The code only (never a message from Meta or n8n), within the column's limit.</summary>
    private static string Code(string? error) => error is null ? "unknown" : error.Length <= 80 ? error : error[..80];
}
