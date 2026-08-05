using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

/// <summary>
/// T-S-MONTH-BALANCE: |resting on prev-month last day − resting on month day 1| among night→morning people.
/// </summary>
public sealed class TsMonthBalance : IRuleEvaluator
{
    public string RuleId => "T-S-MONTH-BALANCE";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T) return RuleResult.Ok(RuleId);

        var people = MonthBoundary.NightToMorningEmployees(ctx).ToList();
        if (people.Count == 0) return RuleResult.Ok(RuleId);

        var prevLast = ctx.Period.FirstDay.AddDays(-1);
        var monthFirst = ctx.Period.FirstDay;

        var restPrev = people.Count(id => ctx.GetState(id, prevLast)?.IsGeneralRest == true);
        var restFirst = people.Count(id => ctx.GetState(id, monthFirst)?.IsGeneralRest == true);
        var diff = Math.Abs(restPrev - restFirst);
        if (diff == 0) return RuleResult.Ok(RuleId);

        return new RuleResult(RuleId, diff,
        [
            new ViolationItem(RuleId, null, monthFirst,
                $"夜轉早人員月底／月初休假人數差 {diff}（前月末 {restPrev}、本月初 {restFirst}）")
        ]);
    }
}
