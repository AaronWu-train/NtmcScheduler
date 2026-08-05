using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

/// <summary>M-S-SUPPORT-FAIR: max−min of cross-station support days within each station group × cycle.</summary>
public sealed class MsSupportFair : IRuleEvaluator
{
    public string RuleId => "M-S-SUPPORT-FAIR";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);

        var cycles = CycleResolver.Intersecting(ctx.Cycles, ctx.Period.FirstDay, ctx.Period.MonthEnd);
        var total = 0;
        var items = new List<ViolationItem>();

        foreach (var cycle in cycles)
        {
            foreach (var g in ctx.Employees.GroupBy(e => StationConfig.GroupOf(e.HomeStation!)))
            {
                var counts = g.Select(e => CountSupport(ctx, e, cycle)).ToList();
                if (counts.Count == 0) continue;
                var spread = counts.Max() - counts.Min();
                if (spread <= 0) continue;
                total += spread;
                items.Add(new ViolationItem(RuleId, null, null,
                    $"跨站支援不均：群組 {g.Key} 週期 {cycle.Start:yyyy-MM-dd} 差距 {spread}"));
            }
        }

        return total == 0 ? RuleResult.Ok(RuleId) : new RuleResult(RuleId, total, items);
    }

    private static int CountSupport(ScheduleContext ctx, EmployeeInfo emp, CycleInfo cycle)
    {
        var n = 0;
        var to = cycle.End < ctx.Period.MonthEnd ? cycle.End : ctx.Period.MonthEnd;
        for (var d = cycle.Start; d <= to; d = d.AddDays(1))
        {
            if (d > ctx.Period.MonthEnd) break;
            var state = ctx.GetState(emp.Id, d);
            if (state?.IsNormalShift != true) continue;
            var station = state.Value.Station ?? emp.HomeStation;
            if (station != emp.HomeStation)
                n++;
        }

        return n;
    }
}
