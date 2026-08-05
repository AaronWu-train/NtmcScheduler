using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation;

/// <summary>
/// Actual work interval (normal shift or X) used by GEN-H-03 rest-gap checks.
/// </summary>
public readonly record struct WorkInterval(
    string EmployeeId,
    DateOnly AttributionDate,
    DateTime Start,
    DateTime End,
    bool IsX);

public static class WorkIntervalBuilder
{
    public static IReadOnlyList<WorkInterval> BuildForEmployee(ScheduleContext ctx, string employeeId)
    {
        var list = new List<WorkInterval>();
        var period = ctx.Period;

        DateOnly? histStart = null;
        if (ctx.Histories.TryGetValue(employeeId, out var hist) && hist.Days.Count > 0)
            histStart = hist.Days.Keys.Min();

        var from = histStart ?? period.FirstDay;
        for (var d = from; d <= period.RangeEnd; d = d.AddDays(1))
        {
            var state = ctx.GetState(employeeId, d);
            if (state is null) continue;
            if (state.Value.Type == DayStateType.Shift && state.Value.Shift is { } shift)
            {
                var (start, end) = ShiftTimeConfig.Interval(ctx.Unit, d, shift);
                list.Add(new WorkInterval(employeeId, d, start, end, IsX: false));
            }
        }

        foreach (var x in ctx.XEvents.Where(e => e.EmployeeId == employeeId))
        {
            list.Add(new WorkInterval(employeeId, x.StartDate, x.Start, x.End, IsX: true));
        }

        return list.OrderBy(i => i.Start).ThenBy(i => i.End).ToList();
    }

    public static DateTime? PreviousEnd(ScheduleContext ctx, string employeeId, DateTime beforeStart)
    {
        DateTime? best = null;
        if (ctx.Histories.TryGetValue(employeeId, out var hist) && hist.LastWorkEnd is { } last
            && last <= beforeStart)
        {
            best = last;
        }

        foreach (var iv in BuildForEmployee(ctx, employeeId))
        {
            if (iv.End <= beforeStart && (best is null || iv.End > best))
                best = iv.End;
        }

        return best;
    }
}
