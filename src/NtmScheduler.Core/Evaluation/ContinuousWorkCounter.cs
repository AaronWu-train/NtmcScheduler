using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation;

/// <summary>
/// GEN-H-02 continuous-work counter:
/// R/R* reset to 0; R1 keeps previous; work days increment. Must stay ≤ 6.
/// </summary>
public static class ContinuousWorkCounter
{
    public static int Compute(DayState state, int previous) => state.Type switch
    {
        DayStateType.Rest or DayStateType.RStar => 0,
        DayStateType.HolidayRest => previous,
        DayStateType.Shift or DayStateType.X => previous + 1,
        _ => previous
    };

    public static IReadOnlyList<(DateOnly Date, int Cw)> Walk(
        ScheduleContext ctx,
        string employeeId,
        DateOnly from,
        DateOnly to)
    {
        var list = new List<(DateOnly, int)>();
        var cw = 0;

        // Seed from day before 'from' if available.
        var seedDate = from.AddDays(-1);
        if (ctx.GetState(employeeId, seedDate) is { } seedState)
        {
            // Rebuild cw up to seedDate from history start.
            cw = RebuildUpTo(ctx, employeeId, seedDate);
        }

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var state = ctx.GetState(employeeId, d);
            if (state is null) break;
            cw = Compute(state.Value, cw);
            list.Add((d, cw));
        }

        return list;
    }

    public static int RebuildUpTo(ScheduleContext ctx, string employeeId, DateOnly upTo)
    {
        var cw = 0;
        DateOnly? start = null;
        if (ctx.Histories.TryGetValue(employeeId, out var hist) && hist.Days.Count > 0)
            start = hist.Days.Keys.Min();
        start ??= ctx.Period.FirstDay;

        for (var d = start.Value; d <= upTo; d = d.AddDays(1))
        {
            var state = ctx.GetState(employeeId, d);
            if (state is null) continue;
            cw = Compute(state.Value, cw);
        }

        return cw;
    }

    public static bool Violates(ScheduleContext ctx, string employeeId)
    {
        foreach (var (_, cw) in Walk(ctx, employeeId, ctx.Period.FirstDay, ctx.Period.RangeEnd))
        {
            if (cw > 6) return true;
        }
        return false;
    }
}
