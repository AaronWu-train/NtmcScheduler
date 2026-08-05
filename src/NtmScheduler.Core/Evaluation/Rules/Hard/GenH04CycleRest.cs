using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Hard;

public sealed class GenH04CycleRest : IRuleEvaluator
{
    public string RuleId => "GEN-H-04";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        var items = new List<ViolationItem>();
        var cycles = CycleResolver.Intersecting(ctx.Cycles, ctx.Period.FirstDay, ctx.Period.RangeEnd);

        foreach (var emp in ctx.Employees)
        {
            foreach (var cycle in cycles)
            {
                EvaluateEmployeeCycle(ctx, emp.Id, cycle, items);
            }
        }
        return RuleResult.From(RuleId, items);
    }

    private static void EvaluateEmployeeCycle(
        ScheduleContext ctx, string empId, CycleInfo cycle, List<ViolationItem> items)
    {
        var rangeEnd = ctx.Period.RangeEnd;
        var monthEnd = ctx.Period.MonthEnd;

        int CountGen(DateOnly upTo) => CountDays(ctx, empId, cycle, upTo, general: true);
        int CountR1(DateOnly upTo) => CountDays(ctx, empId, cycle, upTo, general: false);

        if (cycle.End <= rangeEnd)
        {
            var gen = CountGen(cycle.End);
            var r1 = CountR1(cycle.End);
            if (gen != cycle.RequiredR)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, cycle.End,
                    $"週期一般休假 {gen} ≠ 需求 {cycle.RequiredR}（{cycle.Start:yyyy-MM-dd}～{cycle.End:yyyy-MM-dd}）"));
            }
            if (r1 != cycle.RequiredR1)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, cycle.End,
                    $"週期 R1 {r1} ≠ 需求 {cycle.RequiredR1}（{cycle.Start:yyyy-MM-dd}～{cycle.End:yyyy-MM-dd}）"));
            }
        }
        else
        {
            var gen = CountGen(rangeEnd);
            var r1 = CountR1(rangeEnd);
            if (gen > cycle.RequiredR)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, rangeEnd,
                    $"未結束週期一般休假累積 {gen} 超過 {cycle.RequiredR}"));
            }
            if (r1 > cycle.RequiredR1)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, rangeEnd,
                    $"未結束週期 R1 累積 {r1} 超過 {cycle.RequiredR1}"));
            }

            var remaining = cycle.End.DayNumber - rangeEnd.DayNumber;
            var need = (cycle.RequiredR - gen) + (cycle.RequiredR1 - r1);
            if (need > remaining)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, rangeEnd,
                    $"剩餘 {remaining} 日不足以補足一般休假與 R1（尚需 {need}）"));
            }
        }

        // (c) Proportional reservation across month boundary.
        if (cycle.End > monthEnd)
        {
            var reserved = cycle.ReservedGeneralRest(monthEnd);
            var genToMonth = CountGen(monthEnd);
            var maxAllowed = cycle.RequiredR - reserved;
            if (genToMonth > maxAllowed)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, monthEnd,
                    $"跨月比例保留：截至月底一般休假 {genToMonth} 超過上限 {maxAllowed}（應保留 {reserved}）"));
            }
        }
    }

    private static int CountDays(
        ScheduleContext ctx, string empId, CycleInfo cycle, DateOnly upTo, bool general)
    {
        var n = 0;
        var from = cycle.Start;
        var to = upTo < cycle.End ? upTo : cycle.End;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var state = ctx.GetState(empId, d);
            if (state is null) continue;
            if (general && state.Value.IsGeneralRest) n++;
            if (!general && state.Value.Type == DayStateType.HolidayRest) n++;
        }
        return n;
    }
}
