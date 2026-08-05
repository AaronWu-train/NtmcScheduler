namespace NtmScheduler.Core.Evaluation.Rules.Soft;

/// <summary>GEN-S-WEEKEND-R: max−min of weekend R/R* counts per peer group, summed over intersecting cycles.</summary>
public sealed class GenSWeekendR : IRuleEvaluator
{
    public string RuleId => "GEN-S-WEEKEND-R";

    public RuleResult Evaluate(ScheduleContext ctx) =>
        FairnessRest.Evaluate(ctx, RuleId, weekend: true, "週末 R/R* 不均");
}
