using Office.Domain.Booking;

namespace Office.Domain.Tasks;

/// <summary>
/// B-06: every promise of a contact from the office follows the office's hours (Africa/Cairo): within an hour while it answers,
/// and its next opening in its last hour and after it (today before it opens, tomorrow after it). The hours were 10:00 to 24:00
/// (10:00–22:59 → within an hour; 23:00–23:59 → tomorrow 10:00; 00:00–09:59 → today 10:00). v1.115 (the owner, of a
/// request at 21:52: «هو حد اصلا من المكتب هيرد عليه دلوقتي؟ المفروض يقول له بكره… على حسب المواعيد»): they are the
/// office's hours of the panel (booking.office_*, 10:00–20:00 on the server); without them, 10:00–24:00 as before.
/// </summary>
public static class OfficeHours
{
    /// <summary>B-06's hours before v1.115, and with no office hours set: 10:00 to 24:00.</summary>
    public static MinuteRange Default { get; } = new(600, 1440);

    /// <summary>Africa/Cairo (DST included). "Egypt Standard Time" is the Windows name.</summary>
    public static TimeZoneInfo Cairo { get; } = FindCairo();

    public static DateTimeOffset DueAt(DateTimeOffset now) => DueAt(now, Cairo);

    public static DateTimeOffset DueAt(DateTimeOffset now, TimeZoneInfo zone) => DueAt(now, zone, null);

    /// <summary>v1.115: B-06 with the office's hours of the day (null: <see cref="Default"/>).</summary>
    public static DateTimeOffset DueAt(DateTimeOffset now, MinuteRange? hours) => DueAt(now, Cairo, hours);

    public static DateTimeOffset DueAt(DateTimeOffset now, TimeZoneInfo zone, MinuteRange? hours)
    {
        var (opens, closes) = hours is { IsValid: true } h ? (h.Start, h.End) : (Default.Start, Default.End);
        var lastHour = Math.Max(opens, closes - 60); // a promise «within an hour» never runs past the closing
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var minute = (int)local.TimeOfDay.TotalMinutes;
        if (minute >= opens && minute < lastHour)
        {
            return now.AddHours(1);
        }

        var day = minute >= lastHour ? local.Date.AddDays(1) : local.Date;
        var opening = day.AddMinutes(opens);
        return new DateTimeOffset(opening, zone.GetUtcOffset(opening));
    }

    /// <summary>v1.115: null while the office answers within the hour; else when it answers next (its next opening).</summary>
    public static DateTimeOffset? NextAnswer(DateTimeOffset now, MinuteRange? hours) =>
        DueAt(now, hours) is var due && due == now.AddHours(1) ? null : due;

    private static TimeZoneInfo FindCairo()
    {
        foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        throw new InvalidOperationException("The Africa/Cairo time zone is not installed (tzdata).");
    }
}
