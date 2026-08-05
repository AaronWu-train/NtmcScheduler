using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

public sealed class GenR01 : IRuleEvaluator
{
    public string RuleId => "GEN-R-01";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        var items = new List<ViolationItem>();
        foreach (var (empId, date) in ctx.RStarRequests)
        {
            if (!ctx.Period.IsInTargetMonth(date)) continue;
            var state = ctx.GetState(empId, date);
            if (state?.Type != DayStateType.RStar)
            {
                items.Add(new ViolationItem(RuleId, empId, date, "指定休假 R* 未滿足"));
            }
        }
        return RuleResult.From(RuleId, items);
    }
}
