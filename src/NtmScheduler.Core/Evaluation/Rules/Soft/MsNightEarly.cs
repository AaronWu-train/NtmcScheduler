using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

/// <summary>M-S-NIGHT-EARLY: 夜 → R/R* → 早. R1 in the middle does NOT count.</summary>
public sealed class MsNightEarly : IRuleEvaluator
{
    public string RuleId => "M-S-NIGHT-EARLY";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        return NightRestShiftPattern.Evaluate(ctx, RuleId, ShiftType.Morning,
            "出現夜→R/R*→早");
    }
}

internal static class NightRestShiftPattern
{
    public static RuleResult Evaluate(
        ScheduleContext ctx, string ruleId, ShiftType thirdShift, string message)
    {
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            for (var mid = ctx.Period.FirstDay; mid <= ctx.Period.MonthEnd; mid = mid.AddDays(1))
            {
                var prev = mid.AddDays(-1);
                var next = mid.AddDays(1);
                if (ctx.Period.IsExtensionDay(next)) continue;

                var s0 = ctx.GetState(emp.Id, prev);
                var s1 = ctx.GetState(emp.Id, mid);
                var s2 = ctx.GetState(emp.Id, next);
                if (s0?.IsNormalShift != true || s0.Value.Shift != ShiftType.Night) continue;
                if (s1?.IsGeneralRest != true) continue; // R/R* only, not R1
                if (s2?.IsNormalShift != true || s2.Value.Shift != thirdShift) continue;

                items.Add(new ViolationItem(ruleId, emp.Id, mid, message));
            }
        }

        return RuleResult.From(ruleId, items);
    }
}
