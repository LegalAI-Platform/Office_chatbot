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
/// B-22: the office cancelled an appointment (the panel); this tells the client, from the worker:
/// «الأستاذ عنده ظرف…» with the nearest free times and «استرداد كامل» when booking from WhatsApp is on,
/// the text alone and a task for the staff (office_cancelled) when it is off. A send that fails becomes
/// a task (booking_message_failed). Each appointment is told once (client_notice).
/// </summary>
public sealed class BookingNotices(HazemDbContext db, BookingStore store, BookingDesk desk, ISettingsReader settingsReader, IApprovedTexts texts,
    SendOutbound send, TimeProvider clock)
{
    public const int Batch = 20;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var due = await (from a in db.Appointments.AsNoTracking()
                         join p in db.Persons.AsNoTracking() on a.PersonId equals p.Id
                         where a.ClientNotice == "pending"
                         orderby a.UpdatedAt
                         select new { a.Id, a.PersonId, a.Service, p.WaId }).Take(Batch).ToListAsync(ct);
        if (due.Count == 0)
        {
            return 0;
        }

        var settings = await settingsReader.GetAsync(ct);
        var booking = BookingSettings.From(settings);
        var active = await texts.GetActiveAsync(ct);
        var bookingOn = booking.Enabled && HazemResponder.BookingTextKeys.All(active.ContainsKey);
        var now = clock.GetUtcNow();
        foreach (var a in due)
        {
            ReplyChoices? choices = null;
            if (bookingOn)
            {
                var service = BookingService.ByCode(a.Service) ?? BookingService.PhoneVideo;
                var found = await desk.FindAsync(booking, settings, service, now, now, service.IsUrgent ? BookingDesk.Today(now) : null,
                    booking.Rules.OfferCount, null, ct);
                var rows = found.Slots.Select(s => new ReplyChoice(BookingChoice.Pick(service, s.Start), Title: BookingWords.Row(s.Start, now))).ToList();
                rows.Add(new ReplyChoice(BookingChoice.Refund(a.Id), TextKeys.BtnRefund));
                choices = new ReplyChoices(InteractivePayload.List, rows, TextKeys.BtnSlotsList);
            }

            var composed = ReplyComposer.Compose($"booking:{a.Id:N}:office", null, [new ReplyStep(TextKeys.BookingCancelledByOffice, null, null, choices)],
                active);
            var told = composed.Actions.Count > 0;
            foreach (var action in composed.Actions)
            {
                var r = await send.SendAsync(new OutboundMessage(action.Type, a.WaId, action.IdempotencyKey, "panel-booking", action.Text,
                    Interactive: action.Interactive), ct);
                told &= r.Ok;
            }

            if (!told)
            {
                await store.AddTaskAsync(a.PersonId, $"booking:{a.Id:N}:office", PendingTask.BookingMessageFailed, OfficeHours.DueAt(now), ct);
            }

            if (!bookingOn)
            {
                // The client cannot pick a new time from WhatsApp: the staff arrange it or the refund.
                await store.AddTaskAsync(a.PersonId, $"booking:{a.Id:N}:office", PendingTask.OfficeCancelled, OfficeHours.DueAt(now), ct);
            }

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE hazem.appointments SET client_notice = {(told ? "sent" : "failed")} WHERE id = {a.Id}", ct);
        }

        return due.Count;
    }
}
