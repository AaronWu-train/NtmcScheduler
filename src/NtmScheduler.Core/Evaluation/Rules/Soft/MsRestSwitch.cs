using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

/// <summary>
/// M-S-RESTSWITCH: adjacent effective normal shifts differ with no R/R* between them.
/// R1 is not R/R* — 早,R1,午 still counts. X is skipped from the sequence.
/// </summary>
public sealed class MsRestSwitch : IRuleEvaluator
{
    public string RuleId => "M-S-RESTSWITCH";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        var items = new List<ViolationItem>();

        foreach (var emp in ctx.Employees)
        {
            foreach (var (prev, next, hadGeneralRest) in EffectiveShiftSequence.Transitions(ctx, emp.Id))
            {
                if (prev.Shift == next.Shift) continue;
                if (hadGeneralRest) continue;
                items.Add(new ViolationItem(RuleId, emp.Id, next.Date,
                    $"換班 {prev.Shift.ToDisplay()}→{next.Shift.ToDisplay()} 未經過 R/R*"));
            }
        }

        return RuleResult.From(RuleId, items);
    }
}

internal static class EffectiveShiftSequence
{
    public readonly record struct ShiftAt(DateOnly Date, ShiftType Shift);

    /// <summary>
    /// Yields adjacent effective normal-shift pairs where the later day is in the target month.
    /// hadGeneralRest = any R/R* strictly between the two dates (R1/X do not count as rest).
    /// </summary>
    public static IEnumerable<(ShiftAt Prev, ShiftAt Next, bool HadGeneralRest)> Transitions(
        ScheduleContext ctx, string employeeId)
    {
        ShiftAt? prev = null;
        DateOnly? prevDate = null;

        var histStart = ctx.Histories.TryGetValue(employeeId, out var hist) && hist.Days.Count > 0
            ? hist.Days.Keys.Min()
            : ctx.Period.FirstDay;

        for (var d = histStart; d <= ctx.Period.MonthEnd; d = d.AddDays(1))
        {
            var state = ctx.GetState(employeeId, d);
            if (state is null) continue;
            if (state.Value.Type == DayStateType.X) continue;
            if (!state.Value.IsNormalShift) continue;

            var cur = new ShiftAt(d, state.Value.Shift!.Value);
            if (prev is { } p && prevDate is { } pd && d >= ctx.Period.FirstDay)
            {
                var hadRest = false;
                for (var mid = pd.AddDays(1); mid < d; mid = mid.AddDays(1))
                {
                    var m = ctx.GetState(employeeId, mid);
                    if (m?.IsGeneralRest == true)
                    {
                        hadRest = true;
                        break;
                    }
                }

                yield return (p, cur, hadRest);
            }

            prev = cur;
            prevDate = d;
        }
    }
}
