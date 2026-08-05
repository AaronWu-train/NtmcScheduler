using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Hard;

public sealed class MH01GroupConstraint : IRuleEvaluator
{
    public string RuleId => "M-H-01";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);

        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            var home = emp.HomeStation ?? "";
            var homeGroup = StationConfig.GroupOf(home);
            foreach (var d in ctx.Period.AllDays)
            {
                var state = ctx.GetState(emp.Id, d);
                if (state?.IsNormalShift != true) continue;
                var station = state.Value.Station ?? home;
                if (StationConfig.GroupOf(station) != homeGroup)
                {
                    items.Add(new ViolationItem(RuleId, emp.Id, d,
                        $"跨群組工作禁止：本站 {home}（群組 {homeGroup}），實際 {station}"));
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }
}
