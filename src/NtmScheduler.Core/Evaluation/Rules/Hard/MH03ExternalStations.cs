using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Hard;

public sealed class MH03ExternalStations : IRuleEvaluator
{
    public string RuleId => "M-H-03";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);

        // External fill is not stored as employee rows; CoverageCalculator tracks it.
        // This evaluator flags Coverage external on non-allowed stations if present.
        var items = new List<ViolationItem>();
        foreach (var row in CoverageCalculator.ComputeM(ctx))
        {
            if (row.External > 0 && !StationConfig.ExternalStations.Contains(row.Location))
            {
                items.Add(new ViolationItem(RuleId, null, row.Date,
                    $"{row.Location} 不可使用外派"));
            }
        }
        return RuleResult.From(RuleId, items);
    }
}
