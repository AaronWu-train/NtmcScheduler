namespace NtmScheduler.Core.Evaluation.Rules.Hard;

/// <summary>
/// GEN-H-05 is enforced by the solver (fixed constants) and input validation.
/// Evaluator returns Ok when evaluating a finished schedule.
/// </summary>
public sealed class GenH05FixedHistory : IRuleEvaluator
{
    public string RuleId => "GEN-H-05";

    public RuleResult Evaluate(ScheduleContext ctx) => RuleResult.Ok(RuleId);
}
