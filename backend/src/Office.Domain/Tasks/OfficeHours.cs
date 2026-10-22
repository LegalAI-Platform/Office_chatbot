namespace Office.Domain.Tasks;

/// <summary>
/// B-06: office hours are 10:00 to 24:00 Africa/Cairo, and every promise of a contact from the
/// office follows this table:
///   10:00–22:59 → within an hour;  23:00–23:59 → tomorrow 10:00;  00:00–09:59 → today 10:00.
/// </summary>
public static class OfficeHours
{
    private static readonly TimeSpan OpensAt = TimeSpan.FromHours(10);
    private static readonly TimeSpan LastHourStartsAt = TimeSpan.FromHours(23);

    /// <summary>Africa/Cairo (DST included). "Egypt Standard Time" is the Windows name.</summary>
    public static TimeZoneInfo Cairo { get; } = FindCairo();

    public static DateTimeOffset DueAt(DateTimeOffset now) => DueAt(now, Cairo);

    public static DateTimeOffset DueAt(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var time = local.TimeOfDay;
        if (time >= OpensAt && time < LastHourStartsAt)
        {
            return now.AddHours(1);
        }

        var day = time >= LastHourStartsAt ? local.Date.AddDays(1) : local.Date;
        var opening = day + OpensAt;
        return new DateTimeOffset(opening, zone.GetUtcOffset(opening));
    }

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
