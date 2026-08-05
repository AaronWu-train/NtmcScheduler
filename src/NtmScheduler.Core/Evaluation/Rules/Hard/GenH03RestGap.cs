using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Hard;

public sealed class GenH03RestGap : IRuleEvaluator
{
    public const double MinHours = 11;

    public string RuleId => "GEN-H-03";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            DateTime? lastEnd = ctx.Histories.TryGetValue(emp.Id, out var h) ? h.LastWorkEnd : null;

            for (var d = ctx.Period.FirstDay; d <= ctx.Period.RangeEnd; d = d.AddDays(1))
            {
                foreach (var (start, end) in WorkIntervals(ctx, emp.Id, d))
                {
                    if (lastEnd is { } prev)
                    {
                        var gap = (start - prev).TotalHours;
                        if (gap < MinHours)
                        {
                            items.Add(new ViolationItem(RuleId, emp.Id, d,
                                $"休息僅 {gap:F1} 小時，未滿 11 小時（前次結束 {prev:yyyy-MM-dd HH:mm}）"));
                        }
                    }
                    lastEnd = end;
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }

    public static IEnumerable<(DateTime Start, DateTime End)> WorkIntervals(
        ScheduleContext ctx, string employeeId, DateOnly date)
    {
        var state = ctx.GetState(employeeId, date);
        if (state is null) yield break;

        if (state.Value.Type == DayStateType.X)
        {
            foreach (var x in ctx.XEvents.Where(e => e.EmployeeId == employeeId && e.StartDate == date))
                yield return (x.Start, x.End);
            yield break;
        }

        if (state.Value.IsNormalShift)
            yield return ShiftTimeConfig.Interval(ctx.Unit, date, state.Value.Shift!.Value);
    }
}
