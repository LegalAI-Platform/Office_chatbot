using Microsoft.EntityFrameworkCore;
using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Ports;
using Office.Application.Services;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.UseCases;
using Office.Domain.Tasks;
using Office.Infrastructure.Booking;
using Office.Infrastructure.Persistence;

namespace Office.Infrastructure.Services;

/// <summary>
/// TASK 10 (v1.14, B-16 / B-17): what the client hears about his service order, from the worker (never inside a
/// panel request): the price and where to pay, the one reminder, the price that ran out (the office gets a task),
/// the work started, the file itself (a document Meta reads from a short storage link), the order the office
/// closed, and partial / not received / refunded for an order's payment. A send that fails, outside the 24-hour
/// window too, becomes a task (booking_message_failed); the price, the work started and the file are sent again when
/// the client writes again (the window reopens: hazem.retry_service_notices) or from the panel («ابعت الملف تاني»).
/// </summary>
public sealed class ServiceNotices(HazemDbContext db, BookingStore store, ISettingsReader settingsReader, IApprovedTexts texts,
    IDeliverables deliverables, NoticeSender notices, TimeProvider clock)
{
    public const int Batch = 20;
    private const string Requester = "panel-service";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var closed = await db.Database.SqlQuery<string>($"""SELECT hazem.service_due()::text AS "Value" """).SingleAsync(ct);
        foreach (var c in System.Text.Json.JsonDocument.Parse(closed).RootElement.EnumerateArray())
        {
            await store.AddTaskAsync(c.GetProperty("person").GetGuid(), $"service:{c.GetProperty("id").GetGuid():N}:unpaid", PendingTask.ServiceUnpaid,
                OfficeHours.DueAt(now), ct);
        }

        var orders = await (from o in db.ServiceOrders.AsNoTracking()
                            join p in db.Persons.AsNoTracking() on o.PersonId equals p.Id
                            where o.ClientNotice == "pending"
                            orderby o.UpdatedAt
                            select new { o, p.WaId }).Take(Batch).ToListAsync(ct);
        var payments = await (from pay in db.Payments.AsNoTracking()
                              join o in db.ServiceOrders.AsNoTracking() on pay.ServiceOrderId equals o.Id
                              join p in db.Persons.AsNoTracking() on o.PersonId equals p.Id
                              where pay.ClientNotice == "pending"
                              orderby pay.UpdatedAt
                              select new { pay, o.Kind, o.PersonId, p.WaId }).Take(Batch).ToListAsync(ct);
        if (orders.Count == 0 && payments.Count == 0)
        {
            return 0;
        }

        var office = OfficeInfo.From(await settingsReader.GetAsync(ct));
        var active = await texts.GetActiveAsync(ct);
        foreach (var x in orders)
        {
            var o = x.o;
            var fill = new Dictionary<string, string> { ["service"] = Name(o.Kind) };
            if (o.Price is { } price)
            {
                fill["amount"] = BookingWords.Money(price);
            }

            if (o.DueOn is { } due)
            {
                fill["due"] = DueDay(due, now);
            }

            if (o.PriceUntil is { } until)
            {
                fill["deadline"] = BookingWords.Deadline(until, now);
            }

            string key;
            switch (o.NoticeKind)
            {
                case "priced":
                    key = TextKeys.ServicePriced;
                    fill["delivery"] = ServiceKind.Delivery(o.Delivery);
                    if (office.HasPayment)
                    {
                        fill["instapay"] = office.InstaPay;
                        fill["wallet"] = office.Wallet; // without the office's details the text keeps its slots and is not sent: a task
                    }

                    break;
                case "price_reminder":
                    key = TextKeys.ServicePriceReminder;
                    break;
                case "price_expired":
                    key = TextKeys.ServicePriceExpired;
                    break;
                case "started":
                    key = TextKeys.ServiceStarted;
                    break;
                case "delivered":
                    key = TextKeys.ServiceDelivered;
                    break;
                default:
                    key = TextKeys.ServiceClosed;
                    break;
            }

            // A notice sent again (the client wrote again, or «ابعت الملف تاني») needs a new key: n8n never sends one key twice.
            var id = $"service:{o.Id:N}:{o.NoticeKind}";
            var withFile = o.NoticeKind == "delivered" && o.Delivery == "file";
            var document = withFile && o.FileKey is { } fileKey && deliverables.Configured
                ? new DocumentPayload(deliverables.ReadLink(fileKey), o.FileName ?? "file")
                : null;
            var told = (!withFile || document is not null)
                       && await notices.SendAsync(x.WaId, o.NoticeAttempt == 0 ? id : $"{id}:{o.NoticeAttempt}", Requester,
                           new ReplyStep(key, null, fill), active, ct, document);
            if (!told)
            {
                await store.AddTaskAsync(o.PersonId, id, PendingTask.BookingMessageFailed, OfficeHours.DueAt(now), ct);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.service_orders SET client_notice = {(told ? "sent" : "failed")} WHERE id = {o.Id} AND client_notice = 'pending'", ct);
        }

        foreach (var x in payments)
        {
            var pay = x.pay;
            var (key, at) = pay.NoticeKind switch
            {
                "payment_partial" => (TextKeys.ServicePaymentPartial, pay.ReviewedAt),
                "payment_not_received" => (TextKeys.ServicePaymentNotReceived, pay.ReviewedAt),
                _ => (TextKeys.RefundCompleted, pay.RefundedAt),
            };
            var fill = new Dictionary<string, string>
            {
                ["service"] = Name(x.Kind),
                ["received"] = BookingWords.Money(pay.AmountReceived ?? 0),
                ["amount"] = BookingWords.Money(key == TextKeys.RefundCompleted ? pay.RefundDue ?? 0 : pay.AmountDue),
                ["rest"] = BookingWords.Money(pay.AmountDue - (pay.AmountReceived ?? 0)),
                ["method"] = pay.RefundMethod ?? "",
            };
            var id = $"service-payment:{x.pay.ServiceOrderId:N}:{pay.NoticeKind}:{(at ?? now).ToUnixTimeSeconds()}";
            var told = await notices.SendAsync(x.WaId, id, Requester, new ReplyStep(key, null, fill), active, ct);
            if (!told)
            {
                await store.AddTaskAsync(x.PersonId, id, PendingTask.BookingMessageFailed, OfficeHours.DueAt(now), ct);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.payments SET client_notice = {(told ? "sent" : "failed")} WHERE id = {pay.Id} AND client_notice = 'pending'", ct);
        }

        return orders.Count + payments.Count;
    }


    private static string Name(string kind) => ServiceKind.ByCode(kind)?.Name ?? "الخدمة";

    /// <summary>«بكرة» / «الخميس 29/10»: the delivery day in Hazem's words.</summary>
    public static string DueDay(DateOnly day, DateTimeOffset now) => BookingWords.Day(BookingDesk.Instant(day).AddHours(12), now);
}
