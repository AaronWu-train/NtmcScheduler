using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

public sealed class MsHome : IRuleEvaluator
{
    public string RuleId => "M-S-HOME";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            foreach (var d in ctx.Period.TargetMonthDays)
            {
                var state = ctx.GetState(emp.Id, d);
                if (state?.IsNormalShift != true) continue;
                var station = state.Value.Station ?? emp.HomeStation;
                if (station != emp.HomeStation)
                {
                    items.Add(new ViolationItem(RuleId, emp.Id, d,
                        $"非本站工作：本站 {emp.HomeStation}，實際 {station}"));
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }
}
