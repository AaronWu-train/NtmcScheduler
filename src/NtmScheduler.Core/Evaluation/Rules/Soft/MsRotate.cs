using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

/// <summary>
/// M-S-ROTATE: when effective normal shifts change, prefer 早→午→夜→早.
/// </summary>
public sealed class MsRotate : IRuleEvaluator
{
    public string RuleId => "M-S-ROTATE";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        var items = new List<ViolationItem>();

        foreach (var emp in ctx.Employees)
        {
            foreach (var (prev, next, _) in EffectiveShiftSequence.Transitions(ctx, emp.Id))
            {
                if (prev.Shift == next.Shift) continue;
                if (IsPreferred(prev.Shift, next.Shift)) continue;
                items.Add(new ViolationItem(RuleId, emp.Id, next.Date,
                    $"換班方向非早→午→夜→早：{prev.Shift.ToDisplay()}→{next.Shift.ToDisplay()}"));
            }
        }

        return RuleResult.From(RuleId, items);
    }

    private static bool IsPreferred(ShiftType from, ShiftType to) =>
        (from == ShiftType.Morning && to == ShiftType.Afternoon)
        || (from == ShiftType.Afternoon && to == ShiftType.Night)
        || (from == ShiftType.Night && to == ShiftType.Morning);
}
