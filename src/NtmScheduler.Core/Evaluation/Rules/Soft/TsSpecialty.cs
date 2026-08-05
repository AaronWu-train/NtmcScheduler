using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

public sealed class TsSpecialty : IRuleEvaluator
{
    public string RuleId => "T-S-SPECIALTY";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T) return RuleResult.Ok(RuleId);
        var items = new List<ViolationItem>();
        foreach (var row in CoverageCalculator.ComputeT(ctx))
        {
            foreach (var sp in row.MissingSpecialties)
            {
                items.Add(new ViolationItem(RuleId, null, row.Date,
                    $"{row.Shift.ToDisplay()}班專業「{sp}」當日無正常出勤"));
            }
        }
        return RuleResult.From(RuleId, items);
    }
}
