namespace NtmScheduler.Core.Domain;

/// <summary>
/// Scheduling period: from target month day 1 through the Sunday of the week containing month-end.
/// Extension days are (MonthEnd, RangeEnd].
/// </summary>
public sealed record SchedulePeriod(
    YearMonth TargetMonth,
    DateOnly FirstDay,
    DateOnly MonthEnd,
    DateOnly RangeEnd)
{
    public IEnumerable<DateOnly> AllDays
    {
        get
        {
            for (var d = FirstDay; d <= RangeEnd; d = d.AddDays(1))
                yield return d;
        }
    }

    public IEnumerable<DateOnly> TargetMonthDays
    {
        get
        {
            for (var d = FirstDay; d <= MonthEnd; d = d.AddDays(1))
                yield return d;
        }
    }

    public IEnumerable<DateOnly> ExtensionDays
    {
        get
        {
            for (var d = MonthEnd.AddDays(1); d <= RangeEnd; d = d.AddDays(1))
                yield return d;
        }
    }

    public bool IsExtensionDay(DateOnly date) => date > MonthEnd && date <= RangeEnd;
    public bool IsInRange(DateOnly date) => date >= FirstDay && date <= RangeEnd;
    public bool IsInTargetMonth(DateOnly date) => date >= FirstDay && date <= MonthEnd;
}
