using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Hard;

public sealed class TH01FixedShift : IRuleEvaluator
{
    public string RuleId => "T-H-01";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T) return RuleResult.Ok(RuleId);

        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            foreach (var d in ctx.Period.AllDays)
            {
                var state = ctx.GetState(emp.Id, d);
                if (state?.IsNormalShift != true) continue;

                var expected = ResolveExpectedShift(ctx, emp.Id, d);
                if (expected is null) continue;
                if (state.Value.Shift != expected)
                {
                    items.Add(new ViolationItem(RuleId, emp.Id, d,
                        $"正常班必須為固定班別 {expected.Value.ToDisplay()}，實際為 {state.Value.Shift!.Value.ToDisplay()}"));
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }

    public static ShiftType? ResolveExpectedShift(ScheduleContext ctx, string empId, DateOnly date)
    {
        if (ctx.Period.IsInTargetMonth(date))
        {
            if (ctx.MonthlyShifts is not null && ctx.MonthlyShifts.TryGetValue(empId, out var s))
                return s;
            return null;
        }

        // Extension day: next-month data preferred, else rotate.
        if (ctx.NextMonthShifts is not null && ctx.NextMonthShifts.TryGetValue(empId, out var next))
            return next;
        if (ctx.MonthlyShifts is not null && ctx.MonthlyShifts.TryGetValue(empId, out var cur))
            return cur.NextInRotation();
        return null;
    }
}
