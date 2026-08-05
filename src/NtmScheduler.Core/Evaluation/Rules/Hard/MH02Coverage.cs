using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Hard;

public sealed class MH02Coverage : IRuleEvaluator
{
    public string RuleId => "M-H-02";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);

        var items = new List<ViolationItem>();
        var coverage = CoverageCalculator.ComputeM(ctx);
        foreach (var row in coverage)
        {
            if (row.Assigned + row.External < row.Required)
            {
                items.Add(new ViolationItem(RuleId, null, row.Date,
                    $"{row.Location} {row.Shift.ToDisplay()} 缺額：需求 {row.Required}，內部 {row.Assigned}，外派 {row.External}"));
            }
        }
        return RuleResult.From(RuleId, items);
    }
}
