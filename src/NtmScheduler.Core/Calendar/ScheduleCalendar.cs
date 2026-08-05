using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Calendar;

public static class ScheduleCalendar
{
    /// <summary>
    /// Build period from target month day 1 to the Sunday of the week containing month-end.
    /// Week is Monday–Sunday.
    /// </summary>
    public static SchedulePeriod CreatePeriod(YearMonth targetMonth)
    {
        var first = targetMonth.FirstDay;
        var monthEnd = targetMonth.LastDay;
        var rangeEnd = EndOfWeekSunday(monthEnd);
        return new SchedulePeriod(targetMonth, first, monthEnd, rangeEnd);
    }

    public static DateOnly StartOfWeekMonday(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Mon=0 … Sun=6
        return date.AddDays(-offset);
    }

    public static DateOnly EndOfWeekSunday(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(6 - offset);
    }

    public static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static bool IsWeekday(DateOnly date) => !IsWeekend(date);
}
