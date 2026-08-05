using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

// ============================================================================
// 檢測 T 軟規則。違反量定義與預設順序見 RuleCatalog。
// Solver 端的對應目標編碼見 src/NtmScheduler.Solvers/T/TModelBuilder.cs。
// ============================================================================

/// <summary>T-S-ATTEND（P2）：每班每日出勤低於「班組人數 ÷ 2」的不足量總和。</summary>
public sealed class TsAttend : IRuleEvaluator
{
    public string RuleId => "T-S-ATTEND";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T) return RuleResult.Ok(RuleId);
        var items = new List<ViolationItem>();
        var total = 0;
        foreach (var row in CoverageCalculator.ComputeT(ctx))
        {
            var shortfall = Math.Max(0, row.AttendTarget - row.NormalAttend);
            if (shortfall > 0)
            {
                total += shortfall;
                items.Add(new ViolationItem(RuleId, null, row.Date,
                    $"{row.Shift.ToDisplay()}班出勤 {row.NormalAttend}，目標 {row.AttendTarget}，不足 {shortfall}"));
            }
        }
        return new RuleResult(RuleId, total, items);
    }
}

/// <summary>T-S-SPECIALTY（P2）：每（班×日×非空專業）無人正常出勤則 +1。</summary>
public sealed class TsSpecialty : IRuleEvaluator
{
    public string RuleId => "T-S-SPECIALTY";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T) return RuleResult.Ok(RuleId);
        var items = new List<ViolationItem>();
        foreach (var row in CoverageCalculator.ComputeT(ctx))
        {
            foreach (var sp in row.MissingSpecialties)
            {
                items.Add(new ViolationItem(RuleId, null, row.Date,
                    $"{row.Shift.ToDisplay()}班專業「{sp}」當日無正常出勤"));
            }
        }
        return RuleResult.From(RuleId, items);
    }
}

/// <summary>T-S-ABILITY（P2）：出勤人員平均能力低於 3 的整數缺口總和。</summary>
public sealed class TsAbility : IRuleEvaluator
{
    public string RuleId => "T-S-ABILITY";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T || ctx.MonthlyShifts is null) return RuleResult.Ok(RuleId);

        var items = new List<ViolationItem>();
        var total = 0;
        foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
        {
            var members = ctx.Employees
                .Where(e => ctx.MonthlyShifts.TryGetValue(e.Id, out var s) && s == shift)
                .ToList();

            foreach (var d in ctx.Period.TargetMonthDays)
            {
                var attending = members.Where(e => ctx.GetState(e.Id, d)?.IsNormalShift == true).ToList();
                if (attending.Count == 0) continue;
                var abilitySum = attending.Sum(e => e.Ability ?? 0);
                var deficit = Math.Max(0, 3 * attending.Count - abilitySum);
                if (deficit > 0)
                {
                    total += deficit;
                    var avg = abilitySum / (double)attending.Count;
                    items.Add(new ViolationItem(RuleId, null, d,
                        $"{shift.ToDisplay()}班平均能力 {avg:F2}，低於 3（不足量 {deficit}）"));
                }
            }
        }
        return new RuleResult(RuleId, total, items);
    }
}

/// <summary>T-S-MONTH-REST（P3）：夜轉早人員，最後一夜與第一次早之間的 R／R* 少於 2 的不足量。</summary>
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

/// <summary>T-S-MONTH-BALANCE（P3）：夜轉早人員前月末與本月初休假人數差的絕對值。</summary>
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
