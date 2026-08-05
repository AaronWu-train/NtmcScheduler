namespace NtmScheduler.Core.Evaluation.Rules.Soft;

public sealed class GenSStreak : IRuleEvaluator
{
    public string RuleId => "GEN-S-STREAK";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        var total = 0;
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            var d = SegmentExtractor.StreakDeviation(ctx, emp.Id);
            if (d > 0)
            {
                total += d;
                items.Add(new ViolationItem(RuleId, emp.Id, null,
                    $"連續工作區段偏離量 {d}"));
            }
        }
        return new RuleResult(RuleId, total, items);
    }
}
