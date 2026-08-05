using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

public sealed class MsBlock : IRuleEvaluator
{
    public string RuleId => "M-S-BLOCK";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        var total = 0;
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            var d = BlockExtractor.Evaluate(ctx, emp.Id);
            if (d > 0)
            {
                total += d;
                items.Add(new ViolationItem(RuleId, emp.Id, null,
                    $"同班別區塊偏離量 {d}"));
            }
        }
        return new RuleResult(RuleId, total, items);
    }
}
