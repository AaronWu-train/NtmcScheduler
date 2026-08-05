namespace NtmScheduler.Core.Evaluation.Rules.Hard;

public sealed class GenH02ContinuousWork : IRuleEvaluator
{
    public string RuleId => "GEN-H-02";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            foreach (var (date, cw) in ContinuousWorkCounter.Walk(
                         ctx, emp.Id, ctx.Period.FirstDay, ctx.Period.RangeEnd))
            {
                if (cw > 6)
                {
                    items.Add(new ViolationItem(RuleId, emp.Id, date,
                        $"連續工作計數 {cw} 超過 6（R1 不重置計數）"));
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }
}
