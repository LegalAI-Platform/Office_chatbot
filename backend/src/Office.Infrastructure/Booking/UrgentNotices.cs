using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Ports;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.UseCases;
using Office.Domain.Tasks;
using Office.Infrastructure.Persistence;
using Office.Infrastructure.Services;

namespace Office.Infrastructure.Booking;

/// <summary>
/// v1.33 (B-09): the urgent call-back, from the worker (every round, like <see cref="BookingNotices"/>) and the alert's buttons.
/// <list type="bullet">
/// <item>The alert: an urgent call whose latest receipt has no decision yet and was not alerted, urgent.call_within_minutes
/// before it starts (at once when it is sooner, or for a new receipt after «فيه مشكلة»): the M-03 template urgent_alert to
/// office.lawyer_whatsapp ({{1}} the name, {{2}} the client's number, {{3}} the time, «شوف التحويل» = «ug:see:call»). Once per
/// receipt: a refused send, or no lawyer's number, is an urgent task (urgent_call_failed), never a retry.</item>
/// <item>The escalation: urgent.escalate_after_minutes after the alert and its receipt still without a decision: the same
/// template to office.manager_whatsapp (when written), urgent_waiting to the client (once per call), and an urgent task for
/// the managers. Once per alert (a new alert starts the clock again).</item>
/// <item>The buttons (<see cref="PressAsync"/>): see <see cref="IUrgentCalls"/>. The first decision on the call's latest
/// receipt wins, in one conditional UPDATE (the database decides, never two presses at once).</item>
/// </list>
/// The numbers are never logged (the healthcheck scans the logs): only ids go in a key or a task.
/// </summary>
public sealed class UrgentNotices(HazemDbContext db, BookingStore store, ISettingsReader settingsReader, IApprovedTexts texts,
    SendOutbound send, NoticeSender notices, IDeliverables deliverables, TimeProvider clock, IHostEnvironment env) : IUrgentCalls
{
    public const int Batch = 20;
    private const string Requester = "urgent";
    private const string Ok = "ok";
    private const string Bad = "bad";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var active = await texts.GetActiveAsync(ct);
        if (!UrgentTexts.Keys.All(active.ContainsKey))
        {
            return 0; // the urgent path of 1.32: no alert
        }

        var now = clock.GetUtcNow().ToUniversalTime(); // the database takes UTC only
        var rules = UrgentRules.From(await settingsReader.GetAsync(ct), env.IsEnvironment("test"));
        return await AlertAsync(rules, active, now, ct) + await EscalateAsync(rules, active, now, ct);
    }

    private async Task<int> AlertAsync(UrgentRules rules, IReadOnlyDictionary<string, ApprovedText> active, DateTimeOffset now, CancellationToken ct)
    {
        var soon = now.AddMinutes(rules.CallWithinMinutes);
        var day = now.AddDays(-1);
        var due = await (from a in db.Appointments.AsNoTracking()
                         join pay in db.Payments.AsNoTracking() on (Guid?)a.Id equals pay.AppointmentId
                         let r = db.PaymentReceipts.Where(x => x.PaymentId == pay.Id)
                             .OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id).FirstOrDefault()
                         where a.Service == BookingService.Urgent.Code && a.State == AppointmentInfo.Confirmed
                               && r != null && r.UrgentDecision == null && a.UrgentAlertReceipt != r.Id
                               && a.StartsAt <= soon
                               // a call still to come, or one alerted before whose new receipt came (after «فيه مشكلة»)
                               && (a.EndsAt > now || a.UrgentAlertReceipt != null && a.StartsAt > day)
                         orderby a.StartsAt
                         select new { a.Id, a.PersonId, a.StartsAt, Receipt = r.Id }).Take(Batch).ToListAsync(ct);
        foreach (var a in due)
        {
            // Claimed first: one alert per receipt, even when the send below fails (the office sees a task instead). A new
            // alert starts the escalation's clock again.
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE hazem.appointments SET urgent_alerted_at = {now}, urgent_alert_receipt = {a.Receipt}, urgent_escalated_at = NULL
                 WHERE id = {a.Id} AND state = 'confirmed' AND urgent_alert_receipt IS DISTINCT FROM {a.Receipt}
                """, ct);
            if (claimed == 0)
            {
                continue;
            }

            var ok = rules.LawyerWhatsApp.Length > 0
                     && await AlertOneAsync(rules, rules.LawyerWhatsApp, $"urgent:{a.Receipt:N}:lawyer", a.Id, active, ct);
            if (!ok)
            {
                // B-09.6: until Meta approves the template, or without the lawyer's number: the panel's bell (and e-mail) only.
                await store.AddTaskAsync(a.PersonId, $"urgent:{a.Receipt:N}:lawyer", PendingTask.UrgentCallFailed, now, ct);
            }
        }

        return due.Count;
    }

    private async Task<int> EscalateAsync(UrgentRules rules, IReadOnlyDictionary<string, ApprovedText> active, DateTimeOffset now,
        CancellationToken ct)
    {
        var late = now.AddMinutes(-rules.EscalateAfterMinutes);
        var due = await (from a in db.Appointments.AsNoTracking()
                         join p in db.Persons.AsNoTracking() on a.PersonId equals p.Id
                         join r in db.PaymentReceipts.AsNoTracking() on a.UrgentAlertReceipt equals (Guid?)r.Id
                         where a.Service == BookingService.Urgent.Code && a.State == AppointmentInfo.Confirmed
                               && a.UrgentAlertedAt != null && a.UrgentAlertedAt <= late && a.UrgentEscalatedAt == null
                               && r.UrgentDecision == null
                         orderby a.UrgentAlertedAt
                         select new { a.Id, a.PersonId, Receipt = r.Id, p.WaId, a.UrgentWaitingAt }).Take(Batch).ToListAsync(ct);
        foreach (var a in due)
        {
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE hazem.appointments SET urgent_escalated_at = {now}
                 WHERE id = {a.Id} AND urgent_escalated_at IS NULL AND urgent_alert_receipt = {a.Receipt}
                """, ct);
            if (claimed == 0)
            {
                continue;
            }

            if (rules.ManagerWhatsApp.Length > 0
                && !await AlertOneAsync(rules, rules.ManagerWhatsApp, $"urgent:{a.Receipt:N}:manager", a.Id, active, ct))
            {
                await store.AddTaskAsync(a.PersonId, $"urgent:{a.Receipt:N}:manager", PendingTask.UrgentCallFailed, now, ct);
            }

            if (a.UrgentWaitingAt is null)
            {
                // Once per call: «بنوصّل للمستشار…» is not said again for a second receipt.
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE hazem.appointments SET urgent_waiting_at = {now} WHERE id = {a.Id}", ct);
                var key = $"urgent:{a.Id:N}:waiting";
                if (!await notices.SendAsync(a.WaId, key, Requester, new ReplyStep(TextKeys.UrgentWaiting), active, ct))
                {
                    await store.AddTaskAsync(a.PersonId, key, PendingTask.BookingMessageFailed, now, ct);
                }
            }

            await store.AddTaskAsync(a.PersonId, $"urgent:{a.Receipt:N}:unanswered", PendingTask.UrgentCallUnanswered, now, ct);
        }

        return due.Count;
    }

    public async Task<bool> PressAsync(UrgentPress press, string presserWaId, string turnId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUniversalTime();
        if (press.Kind == UrgentPress.See)
        {
            await ShowLatestAsync(press.Id, presserWaId, turnId, ct);
            return false;
        }

        var decision = press.Kind == UrgentPress.Ok ? Ok : Bad;
        // The first decision on the call's latest receipt wins: a receipt already decided, or no longer the latest (a new one
        // came after «فيه مشكلة»), is left as it is. Two presses at once: the second finds the decision already there.
        var decided = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.payment_receipts r SET urgent_decision = {decision}, urgent_decided_at = {now}
              FROM hazem.payments p JOIN hazem.appointments a ON a.id = p.appointment_id
             WHERE r.id = {press.Id} AND p.id = r.payment_id AND a.service = 'consultation_urgent' AND r.urgent_decision IS NULL
               AND r.id = (SELECT x.id FROM hazem.payment_receipts x WHERE x.payment_id = r.payment_id
                            ORDER BY x.received_at DESC, x.id DESC LIMIT 1)
            """, ct) == 1;
        var call = await (from r in db.PaymentReceipts.AsNoTracking()
                          join p in db.Payments.AsNoTracking() on r.PaymentId equals p.Id
                          join a in db.Appointments.AsNoTracking() on p.AppointmentId equals (Guid?)a.Id
                          join person in db.Persons.AsNoTracking() on a.PersonId equals person.Id
                          where r.Id == press.Id && a.Service == BookingService.Urgent.Code
                          select new { a.Id, a.PersonId, a.StartsAt, PaymentId = p.Id, person.WaId }).FirstOrDefaultAsync(ct);
        if (call is null)
        {
            return false; // not an urgent call's receipt: nothing
        }

        if (!decided)
        {
            // Stale: nothing to the client; the presser gets the receipt still waiting for him, if there is a newer one.
            await ShowLatestAsync(call.Id, presserWaId, turnId, ct);
            return false;
        }

        var active = await texts.GetActiveAsync(ct);
        if (decision == Ok)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.appointments SET urgent_acked_at = {now} WHERE id = {call.Id} AND urgent_acked_at IS NULL", ct);
            var seen = $"urgent:{press.Id:N}:seen";
            if (!await notices.SendAsync(call.WaId, seen, Requester, new ReplyStep(TextKeys.UrgentSeen), active, ct))
            {
                await store.AddTaskAsync(call.PersonId, seen, PendingTask.BookingMessageFailed, now, ct);
            }

            return true;
        }

        // «فيه مشكلة»: as the office's «ما وصلش» (the client's next receipt is attached as usual and alerts the lawyer again),
        // without the worker's own notice: the client is told here, and the office checks the transfer.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.payments SET state = 'not_received', amount_received = NULL, reviewed_at = {now}
             WHERE id = {call.PaymentId} AND state IN ('proof_received', 'duplicate_suspected')
            """, ct);
        var key = $"urgent:{press.Id:N}:bad";
        var fill = new Dictionary<string, string> { ["day"] = BookingWords.Day(call.StartsAt, now), ["time"] = BookingWords.Time(call.StartsAt) };
        if (!await notices.SendAsync(call.WaId, key, Requester, new ReplyStep(TextKeys.PaymentNotReceived, null, fill), active, ct))
        {
            await store.AddTaskAsync(call.PersonId, $"{key}:notice", PendingTask.BookingMessageFailed, now, ct);
        }

        await store.AddTaskAsync(call.PersonId, key, PendingTask.PaymentReviewDue, now, ct);
        return true;
    }

    /// <summary>
    /// «شوف التحويل»: the call's latest receipt, when it has no decision yet, as a document (a signed link Meta reads, T-09:
    /// never its content here), then the alert's words with «التحويل تمام، هكلمه» and «فيه مشكلة». Nothing otherwise.
    /// </summary>
    private async Task ShowLatestAsync(Guid appointmentId, string presserWaId, string turnId, CancellationToken ct)
    {
        var receipt = await (from r in db.PaymentReceipts.AsNoTracking()
                             join p in db.Payments.AsNoTracking() on r.PaymentId equals p.Id
                             join a in db.Appointments.AsNoTracking() on p.AppointmentId equals (Guid?)a.Id
                             where a.Id == appointmentId && a.Service == BookingService.Urgent.Code
                             orderby r.ReceivedAt descending, r.Id descending
                             select new { r.Id, r.Bucket, r.Key, r.Mime, r.UrgentDecision, a.PersonId }).FirstOrDefaultAsync(ct);
        if (receipt is not { UrgentDecision: null })
        {
            return;
        }

        var now = clock.GetUtcNow();
        var active = await texts.GetActiveAsync(ct);
        var fill = await FillAsync(appointmentId, ct);
        var document = receipt.Bucket == S3Deliverables.Bucket && deliverables.Configured
            ? new DocumentPayload(deliverables.ReadLink(receipt.Key), receipt.Mime == "image/png" ? "صورة التحويل.png" : "صورة التحويل.jpg")
            : null;
        // One press, one pair of messages (a second press of «شوف التحويل» sends them again: its own turn's keys).
        var sent = document is not null
                   && await notices.SendAsync(presserWaId, $"urgent:{turnId}:receipt", Requester, new ReplyStep(TextKeys.UrgentAlert, null, fill),
                       active, ct, document)
                   && await notices.SendAsync(presserWaId, $"urgent:{turnId}:decide", Requester, new ReplyStep(TextKeys.UrgentAlert, null, fill,
                       new ReplyChoices(InteractivePayload.Buttons,
                       [
                           new ReplyChoice(UrgentButtons.Ok(receipt.Id), TextKeys.BtnUrgentOk),
                           new ReplyChoice(UrgentButtons.Bad(receipt.Id), TextKeys.BtnUrgentBad),
                       ])), active, ct);
        if (!sent)
        {
            await store.AddTaskAsync(receipt.PersonId, $"urgent:{receipt.Id:N}:receipt", PendingTask.UrgentCallFailed, now, ct);
        }
    }

    /// <summary>
    /// The alert to one number. v1.34 (B-09.6, test only): with <see cref="UrgentRules.AlertInWindow"/>, first the same words
    /// with «شوف التحويل» as a normal message, which Meta delivers when that number wrote to the bot in the last 24 hours;
    /// refused (outside the window, or anything else), the template as always. False when both were refused.
    /// </summary>
    private async Task<bool> AlertOneAsync(UrgentRules rules, string waId, string key, Guid appointmentId,
        IReadOnlyDictionary<string, ApprovedText> active, CancellationToken ct)
    {
        if (rules.AlertInWindow)
        {
            var step = new ReplyStep(TextKeys.UrgentAlert, null, await FillAsync(appointmentId, ct), new ReplyChoices(InteractivePayload.Buttons,
                [new ReplyChoice(UrgentButtons.See(appointmentId), TextKeys.BtnUrgentSee)]));
            if (await notices.SendAsync(waId, $"{key}:message", Requester, step, active, ct))
            {
                return true;
            }
        }

        return await TemplateAsync(waId, key, appointmentId, ct);
    }

    /// <summary>The template urgent_alert, «شوف التحويل» carrying the call's id. False when n8n or Meta refused it.</summary>
    private async Task<bool> TemplateAsync(string waId, string key, Guid appointmentId, CancellationToken ct)
    {
        var fill = await FillAsync(appointmentId, ct);
        var components = new JsonArray(
            new JsonObject
            {
                ["type"] = "body",
                ["parameters"] = new JsonArray(new[] { fill["name"], fill["phone"], fill["time"] }
                    .Select(v => (JsonNode?)new JsonObject { ["type"] = "text", ["text"] = v }).ToArray()),
            },
            new JsonObject
            {
                ["type"] = "button",
                ["sub_type"] = "quick_reply",
                ["index"] = "0",
                ["parameters"] = new JsonArray(new JsonObject { ["type"] = "payload", ["payload"] = UrgentButtons.See(appointmentId) }),
            });
        var r = await send.SendAsync(new OutboundMessage(ActionType.SendTemplate, waId, key, Requester,
            TemplateName: UrgentTexts.Template, Language: "ar", Components: components), ct);
        return r.Ok;
    }

    /// <summary>
    /// {{name}} (the office's name for him, v1.36: the name he gave, else his WhatsApp name marked «(اسم الواتساب)», else
    /// «عميل»), {{phone}} (as the lawyer dials it), {{time}}.
    /// </summary>
    private async Task<Dictionary<string, string>> FillAsync(Guid appointmentId, CancellationToken ct)
    {
        var a = await (from x in db.Appointments.AsNoTracking()
                       join p in db.Persons.AsNoTracking() on x.PersonId equals p.Id
                       where x.Id == appointmentId
                       select new { x.StartsAt, p.WaId, p.OfficeName, p.PreferredCallPhone }).SingleAsync(ct);
        return new Dictionary<string, string>
        {
            ["name"] = UrgentTexts.Name(a.OfficeName, null),
            ["phone"] = UrgentTexts.CallNumber(a.WaId, a.PreferredCallPhone), // v1.37: a username client, the number he gave
            ["time"] = BookingWords.Time(a.StartsAt),
        };
    }
}
