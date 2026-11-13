using Microsoft.EntityFrameworkCore;
using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Ports;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.UseCases;
using Office.Domain.Tasks;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Booking;

/// <summary>
/// What the client is told outside his own turn, from the worker (never inside a panel request):
/// <list type="bullet">
/// <item>B-22: the office cancelled: «المستشار عنده ظرف…» with the nearest free times and «استرداد كامل» when he had
/// paid, the times alone when it was only held (1.13), the text alone and a task (office_cancelled) when booking
/// from WhatsApp is off.</item>
/// <item>B-08.6 (1.13): the one reminder before a hold runs out, and the hold that ran out (hazem.booking_due).</item>
/// <item>B-05 / B-22: partial, not received (an office person's review) and the refund done.</item>
/// <item>1.25 (B-08): «تمام» on the transfer: «تم تأكيد…», with where the lawyer calls or where the office is; and an
/// urgent task for a transfer nobody reviewed when its appointment is soon (the time stays the client's).</item>
/// </list>
/// A send that fails, outside the 24-hour window too (P5), becomes a task (booking_message_failed). Each is told once.
/// </summary>
public sealed class BookingNotices(HazemDbContext db, BookingStore store, BookingDesk desk, ISettingsReader settingsReader, IApprovedTexts texts,
    NoticeSender notices, TimeProvider clock)
{
    public const int Batch = 20;
    private const string Requester = "panel-booking";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        await db.Database.SqlQuery<string>($"""
            SELECT hazem.booking_due({HoldRules.ReminderBeforeMinutes}, {HoldRules.RemindedHold})::text AS "Value"
            """).SingleAsync(ct);
        await AlertUnreviewedAsync(ct);
        await AutoCompleteAsync(ct);

        var due = await (from a in db.Appointments.AsNoTracking()
                         join p in db.Persons.AsNoTracking() on a.PersonId equals p.Id
                         join pay in db.Payments.AsNoTracking() on (Guid?)a.Id equals pay.AppointmentId into pays
                         from pay in pays.DefaultIfEmpty()
                         where a.ClientNotice == "pending"
                         orderby a.UpdatedAt
                         select new { a, p.WaId, Paid = pay != null && pay.ProofAt != null }).Take(Batch).ToListAsync(ct);
        var payments = await (from pay in db.Payments.AsNoTracking()
                              join a in db.Appointments.AsNoTracking() on pay.AppointmentId equals (Guid?)a.Id
                              join p in db.Persons.AsNoTracking() on a.PersonId equals p.Id
                              where pay.ClientNotice == "pending"
                              orderby pay.UpdatedAt
                              select new { pay, a, p.WaId }).Take(Batch).ToListAsync(ct);
        if (due.Count == 0 && payments.Count == 0)
        {
            return 0;
        }

        var settings = await settingsReader.GetAsync(ct);
        var booking = BookingSettings.From(settings);
        var active = await texts.GetActiveAsync(ct);
        var bookingOn = booking.Enabled && HazemResponder.BookingTextKeys.All(active.ContainsKey);
        var now = clock.GetUtcNow();
        foreach (var x in due)
        {
            var a = x.a;
            var kind = a.NoticeKind ?? "office_cancelled";
            var step = kind switch
            {
                "hold_reminder" => new ReplyStep(TextKeys.BookingHoldReminder, null, HoldFill(a, booking, now)),
                "hold_expired" => new ReplyStep(TextKeys.BookingHoldExpired, null, When(a, now)),
                _ => await OfficeCancelledAsync(a, x.Paid, booking, settings, bookingOn, now, ct),
            };
            var told = await notices.SendAsync(x.WaId, $"booking:{a.Id:N}:{Short(kind)}", Requester, step, active, ct);
            if (!told)
            {
                await store.AddTaskAsync(a.PersonId, $"booking:{a.Id:N}:{Short(kind)}", PendingTask.BookingMessageFailed, OfficeHours.DueAt(now), ct, a.Id);
            }

            if (kind == "office_cancelled" && !bookingOn)
            {
                // The client cannot pick a new time from WhatsApp: the staff arrange it or the refund.
                await store.AddTaskAsync(a.PersonId, $"booking:{a.Id:N}:office", PendingTask.OfficeCancelled, OfficeHours.DueAt(now), ct, a.Id);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.appointments SET client_notice = {(told ? "sent" : "failed")} WHERE id = {a.Id} AND client_notice = 'pending' AND notice_kind IS NOT DISTINCT FROM {a.NoticeKind}", ct); // v1.28: only the notice that went
        }

        foreach (var x in payments)
        {
            var (pay, a) = (x.pay, x.a);
            var (key, fill, at) = pay.NoticeKind switch
            {
                "payment_partial" => (TextKeys.PaymentPartial, new Dictionary<string, string>(When(a, now))
                {
                    ["received"] = BookingWords.Money(pay.AmountReceived ?? 0),
                    ["amount"] = BookingWords.Money(pay.AmountDue),
                    ["rest"] = BookingWords.Money(pay.AmountDue - (pay.AmountReceived ?? 0)),
                }, pay.ReviewedAt),
                "payment_not_received" => (TextKeys.PaymentNotReceived, When(a, now), pay.ReviewedAt),
                "payment_verified" => Confirmation(a, booking.Office, now, pay.ReviewedAt),
                _ => (TextKeys.RefundCompleted, new Dictionary<string, string>
                {
                    ["amount"] = BookingWords.Money(pay.RefundDue ?? 0),
                    ["method"] = pay.RefundMethod ?? "",
                }, pay.RefundedAt),
            };
            var id = $"payment:{a.Id:N}:{pay.NoticeKind}:{(at ?? now).ToUnixTimeSeconds()}";
            var told = await notices.SendAsync(x.WaId, id, Requester, new ReplyStep(key, null, fill), active, ct);
            if (!told)
            {
                await store.AddTaskAsync(a.PersonId, id, PendingTask.BookingMessageFailed, OfficeHours.DueAt(now), ct, a.Id);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.payments SET client_notice = {(told ? "sent" : "failed")} WHERE id = {pay.Id} AND client_notice = 'pending' AND notice_kind IS NOT DISTINCT FROM {pay.NoticeKind}", ct); // v1.28
        }

        return due.Count + payments.Count;
    }

    /// <summary>B-22: another time (when booking is on), and «استرداد كامل» only for money that came (1.13).</summary>
    private async Task<ReplyStep> OfficeCancelledAsync(AppointmentRow a, bool paid, BookingSettings booking, HazemSettings settings, bool bookingOn,
        DateTimeOffset now, CancellationToken ct)
    {
        if (!bookingOn)
        {
            return new ReplyStep(TextKeys.BookingCancelledByOffice);
        }

        var service = BookingService.ByCode(a.Service) ?? BookingService.PhoneVideo;
        var found = await desk.FindAsync(booking, settings, service, now, now, service.IsUrgent ? BookingDesk.Today(now) : null,
            booking.Rules.OfferCount, null, ct);
        var rows = found.Slots.Select(s => new ReplyChoice(BookingChoice.Pick(service, s.Start, a.CallApp), Title: BookingWords.Row(s.Start, now))).ToList();
        if (paid)
        {
            rows.Add(new ReplyChoice(BookingChoice.Refund(a.Id), TextKeys.BtnRefund));
        }

        var choices = rows.Count > 0 ? new ReplyChoices(InteractivePayload.List, rows, TextKeys.BtnSlotsList) : null;
        return paid
            ? new ReplyStep(TextKeys.BookingCancelledByOffice, null, null, choices)
            : new ReplyStep(TextKeys.BookingOfficeUnpaid, null, When(a, now), choices);
    }

    /// <summary>
    /// 1.25 (B-08): «تم تأكيد…» after «تمام»: a call says where the lawyer calls, the office (and an Urfi appointment) where it
    /// is and its phone. A detail missing keeps the text from going: the office then gets booking_message_failed.
    /// </summary>
    private static (string Key, Dictionary<string, string> Fill, DateTimeOffset? At) Confirmation(AppointmentRow a, OfficeInfo office,
        DateTimeOffset now, DateTimeOffset? at)
    {
        var fill = When(a, now);
        var service = BookingService.ByCode(a.Service) ?? BookingService.PhoneVideo;
        if (service.IsUrf || service.Code == BookingService.Office.Code)
        {
            Put(fill, "address", office.Address);
            Put(fill, "map", office.MapUrl);
            Put(fill, "office_phone", office.Phone);
            return (TextKeys.BookingConfirmedOffice, fill, at);
        }

        Put(fill, "call_app", CallApp.Words(a.CallApp, office));
        return (TextKeys.BookingConfirmedCall, fill, at);
    }

    private static void Put(Dictionary<string, string> fill, string slot, string? value)
    {
        if (value?.Trim() is { Length: > 0 } v)
        {
            fill[slot] = v;
        }
    }

    /// <summary>
    /// 1.25 (B-08): a confirmed appointment whose transfer is waiting for its review. v1.38: not an urgent call while the
    /// call-back runs (v1.33): the lawyer checks its transfer himself before he calls; the panel's review stays for the
    /// accounts, without a task.
    /// v1.108 (the owner: «مليون حاجة بتحصل»): the review task opens as soon as the receipt waits, so the staff hear it once,
    /// then. It is due booking.review_alert_minutes before the appointment (never less than 10 minutes from now: no task is
    /// born late), and it closes by itself once the transfer is reviewed, whoever reviewed it.
    /// </summary>
    private async Task AlertUnreviewedAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var settings = await settingsReader.GetAsync(ct);
        var minutes = int.TryParse(settings.Get(BookingSettings.ReviewAlertKey), out var m) && m > 0 ? m : BookingSettings.DefaultReviewAlertMinutes;
        var active = await texts.GetActiveAsync(settings, ct);
        var callBack = UrgentTexts.Keys.All(active.ContainsKey);
        var utc = now.ToUniversalTime();
        // One task per booking (its key), in one statement however many wait.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO hazem.staff_tasks (person_id, turn_id, type, due_at, appointment_id)
            SELECT a.person_id, 'booking:' || replace(a.id::text, '-', '') || ':review', {PendingTask.PaymentReviewDue},
                   GREATEST(a.starts_at - make_interval(mins => {minutes}), {utc.AddMinutes(10)}), a.id
              FROM hazem.appointments a JOIN hazem.payments p ON p.appointment_id = a.id
             WHERE a.state = 'confirmed' AND a.starts_at > {utc}
               AND (NOT {callBack} OR a.service <> {BookingService.Urgent.Code})
               AND p.state IN ('proof_received', 'duplicate_suspected')
            ON CONFLICT (turn_id, type) DO NOTHING
            """, ct);

        // Reviewed (verified, partly, not received), refunded or the booking gone: its review task is done.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.staff_tasks t SET state = 'done'
              FROM hazem.appointments a LEFT JOIN hazem.payments p ON p.appointment_id = a.id
             WHERE t.appointment_id = a.id AND t.turn_id = 'booking:' || replace(a.id::text, '-', '') || ':review'
               AND t.state IN ('open', 'in_progress', 'overdue')
               AND (a.state <> 'confirmed' OR p.state IS NULL OR p.state NOT IN ('proof_received', 'duplicate_suspected'))
            """, ct);
    }

    /// <summary>
    /// v1.50 (TASK 19): a confirmed appointment whose end is booking.auto_complete_hours ago and that nobody marked becomes
    /// «خلص (تلقائي)» when its transfer was verified (the SQL function, atomic); one still under review gives the office the
    /// task appointment_unmarked instead, once. A move shifts the same row, so the hours count from the new time.
    /// </summary>
    private async Task AutoCompleteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUniversalTime();
        var settings = await settingsReader.GetAsync(ct);
        var hours = int.TryParse(settings.Get(BookingSettings.AutoCompleteHoursKey), out var h) && h >= 0 ? h : BookingSettings.DefaultAutoCompleteHours;
        if (hours == 0)
        {
            return;
        }

        await db.Database.SqlQuery<Guid>($"""SELECT hazem.auto_complete_appointments({now}, {hours}) AS "Value" """).ToListAsync(ct);
        var passed = now.AddHours(-hours);
        var unreviewed = await (from a in db.Appointments.AsNoTracking()
                                join pay in db.Payments.AsNoTracking() on (Guid?)a.Id equals pay.AppointmentId
                                where a.State == AppointmentInfo.Confirmed && a.EndsAt <= passed && pay.State != PaymentInfo.Verified
                                select new { a.Id, a.PersonId }).Take(Batch).ToListAsync(ct);
        foreach (var x in unreviewed)
        {
            // v1.108: due by the office's hours like any task, never born late
            await store.AddTaskAsync(x.PersonId, $"booking:{x.Id:N}:unmarked", PendingTask.AppointmentUnmarked, OfficeHours.DueAt(now), ct, x.Id);
        }

        // v1.108: a booking marked «خلص» / «ماحضرش», cancelled or done by itself needs nobody any more: the task that waited
        // for that mark, and the urgent call's task, are done.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE hazem.staff_tasks t SET state = 'done'
              FROM hazem.appointments a
             WHERE t.appointment_id = a.id AND a.state <> 'confirmed'
               AND t.type IN ({PendingTask.AppointmentUnmarked}, {PendingTask.UrgentRequest})
               AND t.state IN ('open', 'in_progress', 'overdue')
            """, ct);
    }

    private static Dictionary<string, string> When(AppointmentRow a, DateTimeOffset now) => new()
    {
        ["day"] = BookingWords.Day(a.StartsAt, now),
        ["time"] = BookingWords.Time(a.StartsAt),
    };

    /// <summary>B-08.6: the reminder repeats where to pay; without the office's details it keeps its slots and is not sent.</summary>
    private static Dictionary<string, string> HoldFill(AppointmentRow a, BookingSettings booking, DateTimeOffset now)
    {
        var fill = new Dictionary<string, string>(When(a, now))
        {
            ["deadline"] = BookingWords.Deadline(a.HoldUntil ?? a.StartsAt, now),
            ["amount"] = BookingWords.Money(a.Amount),
        };
        if (booking.Office.HasPayment)
        {
            fill["instapay"] = booking.Office.InstaPay;
            fill["wallet"] = booking.Office.Wallet;
        }

        return fill;
    }

    /// <summary>The idempotency key's word for each notice (office keeps TASK 08's key).</summary>
    private static string Short(string kind) => kind switch
    {
        "hold_reminder" => "reminder",
        "hold_expired" => "expired",
        _ => "office",
    };
}
