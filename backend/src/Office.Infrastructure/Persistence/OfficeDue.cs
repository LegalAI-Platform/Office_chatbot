using Microsoft.EntityFrameworkCore;
using Office.Application.Booking;
using Office.Application.Responders;
using Office.Domain.Tasks;

namespace Office.Infrastructure.Persistence;

/// <summary>
/// v1.115 (the owner, of a client told at 21:52 that someone would answer: «هو حد اصلا من المكتب هيرد عليه دلوقتي؟ المفروض يقول
/// له بكره… على حسب المواعيد»): B-06's due time of an office task with the office's hours of the panel (booking.office_*), as
/// the conversation's own tasks have them. A task made after the office's last hour is due at its next opening, never at night.
/// </summary>
internal static class OfficeDue
{
    public static async Task<DateTimeOffset> AtAsync(HazemDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var hours = await db.Settings.AsNoTracking()
            .Where(s => s.Key == BookingSettings.OfficeStartKey || s.Key == BookingSettings.OfficeEndKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, ct);
        return OfficeHours.DueAt(now, BookingSettings.OfficeWindowFrom(new HazemSettings(hours)));
    }
}
