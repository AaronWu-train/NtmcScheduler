using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Soft;

// ============================================================================
// 站務 M 軟規則。違反量定義與預設順序見 RuleCatalog。
// Solver 端的對應目標編碼見 src/NtmScheduler.Solvers/M/MModelBuilder.cs。
// ============================================================================

/// <summary>M-S-EXT（P2）：外派補足班位數最少。</summary>
public sealed class MsExt : IRuleEvaluator
{
    public string RuleId => "M-S-EXT";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        var external = CoverageCalculator.ComputeM(ctx).Sum(r => r.External);
        if (external == 0) return RuleResult.Ok(RuleId);
        return new RuleResult(RuleId, external,
        [
            new ViolationItem(RuleId, null, null, $"外派班位共 {external} 個")
        ]);
    }
}

/// <summary>M-S-HOME（P2）：非本站正常班日數最少（X 不計）。</summary>
public sealed class MsHome : IRuleEvaluator
{
    public string RuleId => "M-S-HOME";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            foreach (var d in ctx.Period.TargetMonthDays)
            {
                var state = ctx.GetState(emp.Id, d);
                if (state?.IsNormalShift != true) continue;
                var station = state.Value.Station ?? emp.HomeStation;
                if (station != emp.HomeStation)
                {
                    items.Add(new ViolationItem(RuleId, emp.Id, d,
                        $"非本站工作：本站 {emp.HomeStation}，實際 {station}"));
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }
}

/// <summary>M-S-BLOCK（P3）：同班別區塊長度偏離 3–5 次的總量。</summary>
public sealed class MsBlock : IRuleEvaluator
{
    public string RuleId => "M-S-BLOCK";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        var total = 0;
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            var d = BlockExtractor.Evaluate(ctx, emp.Id);
            if (d > 0)
            {
                total += d;
                items.Add(new ViolationItem(RuleId, emp.Id, null,
                    $"同班別區塊偏離量 {d}"));
            }
        }
        return new RuleResult(RuleId, total, items);
    }
}

/// <summary>M-S-NIGHT-EARLY（P3）：避免「夜 → R/R* → 早」。中間為 R1 不算。</summary>
public sealed class MsNightEarly : IRuleEvaluator
{
    public string RuleId => "M-S-NIGHT-EARLY";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);
        return NightRestShiftPattern.Evaluate(ctx, RuleId, ShiftType.Morning,
            "出現夜→R/R*→早");
    }
}

/// <summary>M-S-NIGHT-AFTERNOON（P3）：避免「夜 → R/R* → 午」。中間為 R1 不算。</summary>
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

internal static class NightRestShiftPattern
{
    public static RuleResult Evaluate(
        ScheduleContext ctx, string ruleId, ShiftType thirdShift, string message)
    {
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            for (var mid = ctx.Period.FirstDay; mid <= ctx.Period.MonthEnd; mid = mid.AddDays(1))
            {
                var prev = mid.AddDays(-1);
                var next = mid.AddDays(1);
                if (ctx.Period.IsExtensionDay(next)) continue;

                var s0 = ctx.GetState(emp.Id, prev);
                var s1 = ctx.GetState(emp.Id, mid);
                var s2 = ctx.GetState(emp.Id, next);
                if (s0?.IsNormalShift != true || s0.Value.Shift != ShiftType.Night) continue;
                if (s1?.IsGeneralRest != true) continue; // R/R* only, not R1
                if (s2?.IsNormalShift != true || s2.Value.Shift != thirdShift) continue;

                items.Add(new ViolationItem(ruleId, emp.Id, mid, message));
            }
        }

        return RuleResult.From(ruleId, items);
    }
}

/// <summary>
/// M-S-RESTSWITCH（P3）：相鄰有效正常班不同班且中間無 R／R*。
/// R1 不算休假；X 自序列略過。
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

/// <summary>M-S-ROTATE（P3）：換班方向優先 早→午→夜→早。</summary>
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

/// <summary>M-S-SUPPORT-FAIR（P4）：同群組跨站支援次數 max−min，跨週期相加。</summary>
public sealed class MsSupportFair : IRuleEvaluator
{
    public string RuleId => "M-S-SUPPORT-FAIR";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);

        var cycles = CycleResolver.Intersecting(ctx.Cycles, ctx.Period.FirstDay, ctx.Period.MonthEnd);
        var total = 0;
        var items = new List<ViolationItem>();

        foreach (var cycle in cycles)
        {
            foreach (var g in ctx.Employees.GroupBy(e => StationConfig.GroupOf(e.HomeStation!)))
            {
                var counts = g.Select(e => CountSupport(ctx, e, cycle)).ToList();
                if (counts.Count == 0) continue;
                var spread = counts.Max() - counts.Min();
                if (spread <= 0) continue;
                total += spread;
                items.Add(new ViolationItem(RuleId, null, null,
                    $"跨站支援不均：群組 {g.Key} 週期 {cycle.Start:yyyy-MM-dd} 差距 {spread}"));
            }
        }

        return total == 0 ? RuleResult.Ok(RuleId) : new RuleResult(RuleId, total, items);
    }

    private static int CountSupport(ScheduleContext ctx, EmployeeInfo emp, CycleInfo cycle)
    {
        var n = 0;
        var to = cycle.End < ctx.Period.MonthEnd ? cycle.End : ctx.Period.MonthEnd;
        for (var d = cycle.Start; d <= to; d = d.AddDays(1))
        {
            if (d > ctx.Period.MonthEnd) break;
            var state = ctx.GetState(emp.Id, d);
            if (state?.IsNormalShift != true) continue;
            var station = state.Value.Station ?? emp.HomeStation;
            if (station != emp.HomeStation)
                n++;
        }

        return n;
    }
}
