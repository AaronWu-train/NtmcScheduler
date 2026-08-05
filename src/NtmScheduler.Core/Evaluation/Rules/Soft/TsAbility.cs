using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

public sealed class TsAbility : IRuleEvaluator
{
    public string RuleId => "T-S-ABILITY";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T || ctx.MonthlyShifts is null) return RuleResult.Ok(RuleId);

        var items = new List<ViolationItem>();
        var total = 0;
        foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
        {
            var members = ctx.Employees
                .Where(e => ctx.MonthlyShifts.TryGetValue(e.Id, out var s) && s == shift)
                .ToList();

            foreach (var d in ctx.Period.TargetMonthDays)
            {
                var attending = members.Where(e => ctx.GetState(e.Id, d)?.IsNormalShift == true).ToList();
                if (attending.Count == 0) continue;
                var abilitySum = attending.Sum(e => e.Ability ?? 0);
                var deficit = Math.Max(0, 3 * attending.Count - abilitySum);
                if (deficit > 0)
                {
                    total += deficit;
                    var avg = abilitySum / (double)attending.Count;
                    items.Add(new ViolationItem(RuleId, null, d,
                        $"{shift.ToDisplay()}班平均能力 {avg:F2}，低於 3（不足量 {deficit}）"));
                }
            }
        }
        return new RuleResult(RuleId, total, items);
    }
}
