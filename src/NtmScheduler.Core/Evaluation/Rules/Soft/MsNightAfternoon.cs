using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

/// <summary>M-S-NIGHT-AFTERNOON: 夜 → R/R* → 午. R1 in the middle does NOT count.</summary>
public sealed class MsNightAfternoon : IRuleEvaluator
{
    public string RuleId => "M-S-NIGHT-AFTERNOON";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        return NightRestShiftPattern.Evaluate(ctx, RuleId, ShiftType.Afternoon,
            "出現夜→R/R*→午");
    }
}
