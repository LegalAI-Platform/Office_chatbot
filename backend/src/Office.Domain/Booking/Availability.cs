namespace Office.Domain.Booking;

// B-24 (DECISIONS v1.12): the booking hours come from the panel, not the code. The default hours of
// each weekday, then the exceptions of one date (closed all day, some hours closed, or other hours
// that day), then the booking settings. A free time is inside those hours, clear of every confirmed
// appointment and every event in the lawyer's Google Calendar, with the buffer around them. Every
// time here is a minute of the Cairo day (0–1440); the instants are DateTimeOffset.

/// <summary>Minutes of one day, end excluded. 1440 is midnight at the end of the day (24:00).</summary>
public readonly record struct MinuteRange(int Start, int End)
{
    public const int DayMinutes = 1440;

    public bool IsValid => Start >= 0 && End <= DayMinutes && End > Start;

    public bool Overlaps(MinuteRange other) => Start < other.End && other.Start < End;
}

public enum ExceptionKind
{
    ClosedDay,
    ClosedHours,
    CustomHours,
}

/// <summary>One date's exception. <see cref="Range"/> is null for a day closed all day.</summary>
public sealed record DayException(DateOnly Day, ExceptionKind Kind, MinuteRange? Range);

/// <summary>What the panel shows for a date.</summary>
public enum DayStatus
{
    Open,
    WeeklyOff,
    Closed,
    CustomHours,
    PartlyClosed,
}

/// <summary>The panel's booking settings (B-24, P5).</summary>
public sealed record BookingRules(int BufferMinutes, int MinNoticeHours, int HorizonDays, int? MaxPerDay, int SlotStepMinutes, int OfferCount)
{
    public static BookingRules Default { get; } = new(10, 2, 14, null, 15, 4);

    /// <summary>The limits the panel accepts: anything else is refused, never clamped silently.</summary>
    public string? Problem() =>
        BufferMinutes is < 0 or > 240 ? "buffer_minutes"
        : MinNoticeHours is < 0 or > 168 ? "min_notice_hours"
        : HorizonDays is < 1 or > 180 ? "horizon_days"
        : MaxPerDay is < 1 or > 100 ? "max_per_day"
        : SlotStepMinutes is not (5 or 10 or 15 or 20 or 30 or 60) ? "slot_step_minutes"
        : OfferCount is < 1 or > 9 ? "offer_count"
        : null;
}

/// <summary>The default hours of the week: one or more ranges per weekday, none = a day off.</summary>
public sealed class WeeklyHours
{
    private readonly Dictionary<DayOfWeek, IReadOnlyList<MinuteRange>> _days;

    private WeeklyHours(Dictionary<DayOfWeek, IReadOnlyList<MinuteRange>> days) => _days = days;

    public IReadOnlyList<MinuteRange> On(DayOfWeek day) => _days.TryGetValue(day, out var r) ? r : [];

    /// <summary>Refuses a range outside the day, or two ranges of one day that overlap (the panel says which).</summary>
    public static (WeeklyHours? Hours, string? Problem) Create(IEnumerable<(DayOfWeek Day, MinuteRange Range)> ranges)
    {
        var days = new Dictionary<DayOfWeek, IReadOnlyList<MinuteRange>>();
        foreach (var group in ranges.GroupBy(r => r.Day))
        {
            var sorted = group.Select(r => r.Range).OrderBy(r => r.Start).ToList();
            if (sorted.Any(r => !r.IsValid))
            {
                return (null, "invalid_range");
            }

            for (var i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Start < sorted[i - 1].End)
                {
                    return (null, "overlapping_ranges");
                }
            }

            days[group.Key] = sorted;
        }

        return (new WeeklyHours(days), null);
    }
}

/// <summary>The default week plus the exceptions of some dates.</summary>
public sealed class Schedule(WeeklyHours weekly, IReadOnlyList<DayException> exceptions)
{
    public WeeklyHours Weekly { get; } = weekly;

    public IReadOnlyList<DayException> Exceptions { get; } = exceptions;

    /// <summary>The bookable ranges of a date, sorted and merged: the exception of the day wins over the week.</summary>
    public IReadOnlyList<MinuteRange> HoursOn(DateOnly day)
    {
        var own = Exceptions.Where(e => e.Day == day).ToList();
        if (own.Any(e => e.Kind == ExceptionKind.ClosedDay))
        {
            return [];
        }

        var custom = own.Where(e => e.Kind == ExceptionKind.CustomHours && e.Range is not null).Select(e => e.Range!.Value).ToList();
        var open = Merge(custom.Count > 0 ? custom : Weekly.On(day.DayOfWeek));
        foreach (var closed in own.Where(e => e.Kind == ExceptionKind.ClosedHours && e.Range is not null).Select(e => e.Range!.Value))
        {
            open = Subtract(open, closed);
        }

        return open;
    }

    public DayStatus StatusOn(DateOnly day)
    {
        var own = Exceptions.Where(e => e.Day == day).ToList();
        return own.Any(e => e.Kind == ExceptionKind.ClosedDay) ? DayStatus.Closed
            : own.Any(e => e.Kind == ExceptionKind.CustomHours) ? DayStatus.CustomHours
            : own.Any(e => e.Kind == ExceptionKind.ClosedHours) ? DayStatus.PartlyClosed
            : Weekly.On(day.DayOfWeek).Count == 0 ? DayStatus.WeeklyOff
            : DayStatus.Open;
    }

    private static List<MinuteRange> Merge(IEnumerable<MinuteRange> ranges)
    {
        var result = new List<MinuteRange>();
        foreach (var r in ranges.Where(r => r.IsValid).OrderBy(r => r.Start))
        {
            if (result.Count > 0 && r.Start <= result[^1].End)
            {
                result[^1] = result[^1] with { End = Math.Max(result[^1].End, r.End) };
            }
            else
            {
                result.Add(r);
            }
        }

        return result;
    }

    private static List<MinuteRange> Subtract(List<MinuteRange> open, MinuteRange closed)
    {
        var result = new List<MinuteRange>();
        foreach (var r in open)
        {
            if (!r.Overlaps(closed))
            {
                result.Add(r);
                continue;
            }

            if (r.Start < closed.Start)
            {
                result.Add(r with { End = closed.Start });
            }

            if (closed.End < r.End)
            {
                result.Add(r with { Start = closed.End });
            }
        }

        return result;
    }
}

/// <summary>A time Hazem may offer.</summary>
public sealed record Slot(DateTimeOffset Start, DateTimeOffset End);

/// <summary>What to look for.</summary>
/// <param name="After">Only times that start strictly after this instant («مواعيد تانية» pages on).</param>
/// <param name="Urgent">B-09: no minimum notice.</param>
/// <param name="OnlyDay">One Cairo date only (the urgent path: today, then tomorrow).</param>
public sealed record SlotQuery(int DurationMinutes, DateTimeOffset After, bool Urgent, DateOnly? OnlyDay, int Limit);

public static class Availability
{
    /// <summary>
    /// The first free times, in order. <paramref name="booked"/>: the confirmed appointments
    /// (each one also counts for <see cref="BookingRules.MaxPerDay"/> on its Cairo date);
    /// <paramref name="busy"/>: the lawyer's calendar. Both are kept clear by the buffer.
    /// </summary>
    public static IReadOnlyList<Slot> Find(Schedule schedule, BookingRules rules, SlotQuery query,
        IReadOnlyList<BusyInterval> booked, IReadOnlyList<BusyInterval> busy, DateTimeOffset now, TimeZoneInfo zone)
    {
        var result = new List<Slot>();
        if (query.Limit <= 0 || query.DurationMinutes <= 0)
        {
            return result;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var lastDay = today.AddDays(rules.HorizonDays);
        var earliest = query.Urgent ? now : now.AddHours(rules.MinNoticeHours);
        var buffer = TimeSpan.FromMinutes(rules.BufferMinutes);
        var step = Math.Max(rules.SlotStepMinutes, 1);
        var from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(query.After > now ? query.After : now, zone).DateTime);
        var firstDay = query.OnlyDay ?? from;
        var stopDay = query.OnlyDay ?? lastDay;
        if (query.OnlyDay is { } only && (only < today || only > lastDay))
        {
            return result;
        }

        for (var day = firstDay; day <= stopDay; day = day.AddDays(1))
        {
            if (rules.MaxPerDay is { } max && booked.Count(b => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(b.Start, zone).DateTime) == day) >= max)
            {
                continue;
            }

            foreach (var range in schedule.HoursOn(day))
            {
                var first = (range.Start + step - 1) / step * step; // the first start on the step
                for (var minute = first; minute + query.DurationMinutes <= range.End; minute += step)
                {
                    if (At(day, minute, zone) is not { } start)
                    {
                        continue; // a local time that does not exist (the spring change of the clock)
                    }

                    var end = start.AddMinutes(query.DurationMinutes);
                    if (start < earliest || start <= query.After)
                    {
                        continue;
                    }

                    if (booked.Concat(busy).Any(b => b.Overlaps(start - buffer, end + buffer)))
                    {
                        continue;
                    }

                    result.Add(new Slot(start, end));
                    if (result.Count == query.Limit)
                    {
                        return result;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>The same rules for one exact time: is it still free (B-08.3, just before booking)?</summary>
    public static bool IsFree(Schedule schedule, BookingRules rules, DateTimeOffset start, int durationMinutes, bool urgent,
        IReadOnlyList<BusyInterval> booked, IReadOnlyList<BusyInterval> busy, DateTimeOffset now, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
        var slots = Find(schedule, rules, new SlotQuery(durationMinutes, start.AddTicks(-1), urgent, day, 1), booked, busy, now, zone);
        return slots.Count == 1 && slots[0].Start == start;
    }

    /// <summary>
    /// v1.33 (B-09): the first moment the lawyer is free for an urgent call of <paramref name="durationMinutes"/>, from
    /// <paramref name="now"/> (rounded up to the minute): inside the panel's hours (the exceptions too), clear of every taken
    /// appointment (held or confirmed: a held unpaid booking is busy) and every calendar event, with the buffer around them.
    /// Not on the slot grid and no minimum notice; the next days up to the horizon when today has no room. Null: none.
    /// </summary>
    public static DateTimeOffset? FirstFree(Schedule schedule, BookingRules rules, int durationMinutes,
        IReadOnlyList<BusyInterval> booked, IReadOnlyList<BusyInterval> busy, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (durationMinutes <= 0)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var buffer = TimeSpan.FromMinutes(rules.BufferMinutes);
        var length = TimeSpan.FromMinutes(durationMinutes);
        var earliest = CeilMinute(now);
        var taken = booked.Concat(busy).ToList();
        for (var day = today; day <= today.AddDays(rules.HorizonDays); day = day.AddDays(1))
        {
            if (rules.MaxPerDay is { } max && booked.Count(b => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(b.Start, zone).DateTime) == day) >= max)
            {
                continue;
            }

            foreach (var range in schedule.HoursOn(day))
            {
                if (At(day, range.Start, zone) is not { } open || At(day, range.End, zone) is not { } close)
                {
                    continue; // a range that starts or ends at a time the clock skips (DST): left out
                }

                var start = open > earliest ? open : earliest;
                while (start + length <= close)
                {
                    var clash = taken.Where(b => b.Overlaps(start - buffer, start + length + buffer)).ToList();
                    if (clash.Count == 0)
                    {
                        return start;
                    }

                    start = CeilMinute(clash.Max(b => b.End) + buffer); // right after the last thing in the way, and its buffer
                }
            }
        }

        return null;
    }

    /// <summary>
    /// v1.40: the panel's hours are open at <paramref name="at"/> (its day's ranges, the exceptions too). Closed (the night, a
    /// day off) → the lawyer is not working now, whatever the calendar date of his first free moment.
    /// </summary>
    public static bool IsOpen(Schedule schedule, DateTimeOffset at, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
        return schedule.HoursOn(day).Any(r => At(day, r.Start, zone) is { } open && At(day, r.End, zone) is { } close && open <= at && at < close);
    }

    /// <summary>The instant rounded up to a whole minute.</summary>
    public static DateTimeOffset CeilMinute(DateTimeOffset at)
    {
        var minute = TimeSpan.TicksPerMinute;
        var ticks = (at.UtcTicks + minute - 1) / minute * minute;
        return new DateTimeOffset(ticks, TimeSpan.Zero).ToOffset(at.Offset);
    }

    /// <summary>The instant of a Cairo date and minute; null when the clock skips it (DST).</summary>
    public static DateTimeOffset? At(DateOnly day, int minute, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue).AddMinutes(minute);
        if (zone.IsInvalidTime(local))
        {
            return null;
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
