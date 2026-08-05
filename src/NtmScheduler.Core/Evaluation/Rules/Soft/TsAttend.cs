using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

public sealed class TsAttend : IRuleEvaluator
{
    public string RuleId => "T-S-ATTEND";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T) return RuleResult.Ok(RuleId);
        var items = new List<ViolationItem>();
        var total = 0;
        foreach (var row in CoverageCalculator.ComputeT(ctx))
        {
            var shortfall = Math.Max(0, row.AttendTarget - row.NormalAttend);
            if (shortfall > 0)
            {
                total += shortfall;
                items.Add(new ViolationItem(RuleId, null, row.Date,
                    $"{row.Shift.ToDisplay()}班出勤 {row.NormalAttend}，目標 {row.AttendTarget}，不足 {shortfall}"));
            }
        }
        return new RuleResult(RuleId, total, items);
    }
}
