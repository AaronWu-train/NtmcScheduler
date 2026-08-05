using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation;

/// <summary>Per-employee rest counters for the wide table / coverage panel.</summary>
public static class RestStatsCalculator
{
    public static RestStatsDto Compute(ScheduleContext ctx, string empId)
    {
        var monthStart = ctx.Period.FirstDay;
        var monthEnd = ctx.Period.MonthEnd;

        var monthGen = 0;
        var monthR1 = 0;
        for (var d = monthStart; d <= monthEnd; d = d.AddDays(1))
        {
            var s = ctx.GetState(empId, d);
            if (s is null) continue;
            if (s.Value.IsGeneralRest) monthGen++;
            if (s.Value.Type == DayStateType.HolidayRest) monthR1++;
        }

        var cycle = CycleResolver.Find(ctx.Cycles, monthStart)
            ?? CycleResolver.Intersecting(ctx.Cycles, monthStart, ctx.Period.RangeEnd).FirstOrDefault();
        if (cycle is null)
            return new RestStatsDto(empId, monthGen, monthR1, 0, 0, 0, 0, 0);

        var cycleGen = 0;
        var cycleR1 = 0;
        var to = cycle.End <= ctx.Period.RangeEnd ? cycle.End : ctx.Period.RangeEnd;
        for (var d = cycle.Start; d <= to; d = d.AddDays(1))
        {
            var s = ctx.GetState(empId, d);
            if (s is null) continue;
            if (s.Value.IsGeneralRest) cycleGen++;
            if (s.Value.Type == DayStateType.HolidayRest) cycleR1++;
        }

        return new RestStatsDto(
            empId,
            monthGen,
            monthR1,
            cycleGen,
            cycleR1,
            cycle.RequiredR,
            cycle.RequiredR1,
            cycle.ReservedGeneralRest(monthEnd));
    }
}
