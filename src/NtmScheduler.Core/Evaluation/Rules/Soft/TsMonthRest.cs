using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

/// <summary>
/// T-S-MONTH-REST: night→morning rotators need ≥2 R/R* between last night and first morning.
/// </summary>
public sealed class TsMonthRest : IRuleEvaluator
{
    public string RuleId => "T-S-MONTH-REST";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T) return RuleResult.Ok(RuleId);

        var items = new List<ViolationItem>();
        var total = 0;
        foreach (var empId in MonthBoundary.NightToMorningEmployees(ctx))
        {
            if (!TryFindWindow(ctx, empId, out var lastNight, out var firstMorning))
                continue;

            var n = 0;
            for (var d = lastNight.AddDays(1); d < firstMorning; d = d.AddDays(1))
            {
                if (ctx.GetState(empId, d)?.IsGeneralRest == true)
                    n++;
            }

            var deficit = Math.Max(0, 2 - n);
            if (deficit > 0)
            {
                total += deficit;
                items.Add(new ViolationItem(RuleId, empId, firstMorning,
                    $"夜轉早休假不足：僅 {n} 日 R/R*，不足量 {deficit}"));
            }
        }

        return new RuleResult(RuleId, total, items);
    }

    private static bool TryFindWindow(
        ScheduleContext ctx, string empId, out DateOnly lastNight, out DateOnly firstMorning)
    {
        lastNight = default;
        firstMorning = default;
        DateOnly? ln = null;
        DateOnly? fm = null;

        var histStart = ctx.Histories.TryGetValue(empId, out var hist) && hist.Days.Count > 0
            ? hist.Days.Keys.Min()
            : ctx.Period.FirstDay.AddDays(-31);

        for (var d = histStart; d < ctx.Period.FirstDay; d = d.AddDays(1))
        {
            var s = ctx.GetState(empId, d);
            if (s?.IsNormalShift == true && s.Value.Shift == ShiftType.Night)
                ln = d;
        }

        foreach (var d in ctx.Period.TargetMonthDays)
        {
            var s = ctx.GetState(empId, d);
            if (s?.IsNormalShift == true && s.Value.Shift == ShiftType.Morning)
            {
                fm = d;
                break;
            }
        }

        if (ln is null || fm is null) return false;
        lastNight = ln.Value;
        firstMorning = fm.Value;
        return true;
    }
}

internal static class MonthBoundary
{
    public static IEnumerable<string> NightToMorningEmployees(ScheduleContext ctx)
    {
        if (ctx.MonthlyShifts is null || ctx.PreviousMonthShifts is null)
            yield break;

        foreach (var emp in ctx.Employees)
        {
            if (!ctx.PreviousMonthShifts.TryGetValue(emp.Id, out var prev)) continue;
            if (!ctx.MonthlyShifts.TryGetValue(emp.Id, out var cur)) continue;
            if (prev == ShiftType.Night && cur == ShiftType.Morning)
                yield return emp.Id;
        }
    }
}
