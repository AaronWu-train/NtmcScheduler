namespace NtmScheduler.Core.Evaluation;

public interface IRuleEvaluator
{
    string RuleId { get; }
    RuleResult Evaluate(ScheduleContext ctx);
}
