using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Hard;

public sealed class GenH01DailyUnique : IRuleEvaluator
{
    public string RuleId => "GEN-H-01";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            foreach (var d in ctx.Period.AllDays)
            {
                var state = ctx.GetState(emp.Id, d);
                if (state is null)
                {
                    items.Add(new ViolationItem(RuleId, emp.Id, d, "缺少當日狀態"));
                    continue;
                }

                // X start date must not also be a normal/rest state in assignments.
                var xOnDay = ctx.XEvents.Any(x => x.EmployeeId == emp.Id && x.StartDate == d);
                if (xOnDay && state.Value.Type != DayStateType.X)
                {
                    items.Add(new ViolationItem(RuleId, emp.Id, d,
                        "X 開始日不得同時排其他狀態"));
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }
}
