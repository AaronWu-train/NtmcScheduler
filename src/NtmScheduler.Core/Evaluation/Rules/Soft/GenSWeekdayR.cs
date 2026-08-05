using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

/// <summary>GEN-S-WEEKDAY-R: max−min of weekday R/R* counts per peer group, summed over intersecting cycles.</summary>
public sealed class GenSWeekdayR : IRuleEvaluator
{
    public string RuleId => "GEN-S-WEEKDAY-R";

    public RuleResult Evaluate(ScheduleContext ctx) =>
        FairnessRest.Evaluate(ctx, RuleId, weekend: false, "平日 R/R* 不均");
}

internal static class FairnessRest
{
    public static RuleResult Evaluate(ScheduleContext ctx, string ruleId, bool weekend, string label)
    {
        var cycles = CycleResolver.Intersecting(ctx.Cycles, ctx.Period.FirstDay, ctx.Period.MonthEnd);
        var total = 0;
        var items = new List<ViolationItem>();

        foreach (var cycle in cycles)
        {
            foreach (var group in PeerGroups(ctx))
            {
                var counts = group.MemberIds.Select(id =>
                    CountGeneralRest(ctx, id, cycle, weekend)).ToList();
                if (counts.Count == 0) continue;
                var spread = counts.Max() - counts.Min();
                if (spread <= 0) continue;
                total += spread;
                items.Add(new ViolationItem(ruleId, null, null,
                    $"{label}：群組 {group.Key} 週期 {cycle.Start:yyyy-MM-dd} 差距 {spread}"));
            }
        }

        return total == 0 ? RuleResult.Ok(ruleId) : new RuleResult(ruleId, total, items);
    }

    private static int CountGeneralRest(
        ScheduleContext ctx, string empId, CycleInfo cycle, bool weekend)
    {
        var n = 0;
        // History in cycle + target-month days in cycle; extension days excluded (D-13 / §6).
        var to = cycle.End < ctx.Period.MonthEnd ? cycle.End : ctx.Period.MonthEnd;
        var from = cycle.Start;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d > ctx.Period.MonthEnd) break;
            // Only count days that are in history or target month (not future beyond month).
            if (d >= ctx.Period.FirstDay && d > ctx.Period.MonthEnd) continue;
            if (weekend ? !ScheduleCalendar.IsWeekend(d) : !ScheduleCalendar.IsWeekday(d))
                continue;
            if (ctx.GetState(empId, d)?.IsGeneralRest == true)
                n++;
        }

        return n;
    }

    private static IEnumerable<(string Key, IReadOnlyList<string> MemberIds)> PeerGroups(ScheduleContext ctx)
    {
        if (ctx.Unit == Unit.M)
        {
            foreach (var g in ctx.Employees.GroupBy(e => StationConfig.GroupOf(e.HomeStation!)))
                yield return (g.Key, g.Select(e => e.Id).ToList());
        }
        else if (ctx.MonthlyShifts is not null)
        {
            foreach (var g in ctx.Employees.GroupBy(e =>
                         ctx.MonthlyShifts.TryGetValue(e.Id, out var s) ? s : (ShiftType?)null))
            {
                if (g.Key is null) continue;
                yield return (g.Key.Value.ToDisplay(), g.Select(e => e.Id).ToList());
            }
        }
    }
}
