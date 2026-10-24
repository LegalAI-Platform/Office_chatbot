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
/// <item>B-22: the office cancelled: «الأستاذ عنده ظرف…» with the nearest free times and «استرداد كامل» when he had
/// paid, the times alone when it was only held (1.13), the text alone and a task (office_cancelled) when booking
/// from WhatsApp is off.</item>
/// <item>B-08.6 (1.13): the one reminder before a hold runs out, and the hold that ran out (hazem.booking_due).</item>
/// <item>B-05 / B-22: partial, not received (an office person's review) and the refund done.</item>
/// </list>
/// A send that fails, outside the 24-hour window too (P5), becomes a task (booking_message_failed). Each is told once.
/// </summary>
public sealed class BookingNotices(HazemDbContext db, BookingStore store, BookingDesk desk, ISettingsReader settingsReader, IApprovedTexts texts,
    SendOutbound send, TimeProvider clock)
{
    public const int Batch = 20;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        await db.Database.SqlQuery<string>($"""
            SELECT hazem.booking_due({HoldRules.ReminderBeforeMinutes}, {HoldRules.RemindedHold})::text AS "Value"
            """).SingleAsync(ct);

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
            var told = await SendAsync(x.WaId, $"booking:{a.Id:N}:{Short(kind)}", step, active, ct);
            if (!told)
            {
                await store.AddTaskAsync(a.PersonId, $"booking:{a.Id:N}:{Short(kind)}", PendingTask.BookingMessageFailed, OfficeHours.DueAt(now), ct);
            }

            if (kind == "office_cancelled" && !bookingOn)
            {
                // The client cannot pick a new time from WhatsApp: the staff arrange it or the refund.
                await store.AddTaskAsync(a.PersonId, $"booking:{a.Id:N}:office", PendingTask.OfficeCancelled, OfficeHours.DueAt(now), ct);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.appointments SET client_notice = {(told ? "sent" : "failed")} WHERE id = {a.Id} AND client_notice = 'pending'", ct);
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
                _ => (TextKeys.RefundCompleted, new Dictionary<string, string>
                {
                    ["amount"] = BookingWords.Money(pay.RefundDue ?? 0),
                    ["method"] = pay.RefundMethod ?? "",
                }, pay.RefundedAt),
            };
            var id = $"payment:{a.Id:N}:{pay.NoticeKind}:{(at ?? now).ToUnixTimeSeconds()}";
            var told = await SendAsync(x.WaId, id, new ReplyStep(key, null, fill), active, ct);
            if (!told)
            {
                await store.AddTaskAsync(a.PersonId, id, PendingTask.BookingMessageFailed, OfficeHours.DueAt(now), ct);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.payments SET client_notice = {(told ? "sent" : "failed")} WHERE id = {pay.Id} AND client_notice = 'pending'", ct);
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
        var rows = found.Slots.Select(s => new ReplyChoice(BookingChoice.Pick(service, s.Start), Title: BookingWords.Row(s.Start, now))).ToList();
        if (paid)
        {
            rows.Add(new ReplyChoice(BookingChoice.Refund(a.Id), TextKeys.BtnRefund));
        }

        var choices = rows.Count > 0 ? new ReplyChoices(InteractivePayload.List, rows, TextKeys.BtnSlotsList) : null;
        return paid
            ? new ReplyStep(TextKeys.BookingCancelledByOffice, null, null, choices)
            : new ReplyStep(TextKeys.BookingOfficeUnpaid, null, When(a, now), choices);
    }

    private async Task<bool> SendAsync(string waId, string id, ReplyStep step, IReadOnlyDictionary<string, ApprovedText> active, CancellationToken ct)
    {
        var composed = ReplyComposer.Compose(id, null, [step], active);
        var told = composed.Actions.Count > 0;
        foreach (var action in composed.Actions)
        {
            var r = await send.SendAsync(new OutboundMessage(action.Type, waId, action.IdempotencyKey, "panel-booking", action.Text,
                Interactive: action.Interactive), ct);
            told &= r.Ok;
        }

        return told;
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
