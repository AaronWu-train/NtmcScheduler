using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

public sealed class MsExt : IRuleEvaluator
{
    public string RuleId => "M-S-EXT";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        var external = CoverageCalculator.ComputeM(ctx).Sum(r => r.External);
        if (external == 0) return RuleResult.Ok(RuleId);
        return new RuleResult(RuleId, external,
        [
            new ViolationItem(RuleId, null, null, $"外派班位共 {external} 個")
        ]);
    }
}
