using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation.Rules.Hard;

// ============================================================================
// 全部 P0 硬規則（違反即非法班表）。一條規則一個 class，全部集中在本檔。
// Solver 端的對應 CP-SAT 約束見 src/NtmScheduler.Solvers/（HardConstraintEncoder 與 ModelBuilder）。
// ============================================================================

/// <summary>GEN-H-01：每人每日恰好一個狀態；X 開始日不得再排其他狀態。</summary>
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

/// <summary>GEN-H-02：任意兩次 R／R* 之間工作日 ≤ 6；R1 不重置計數。</summary>
public sealed class GenH02ContinuousWork : IRuleEvaluator
{
    public string RuleId => "GEN-H-02";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            foreach (var (date, cw) in ContinuousWorkCounter.Walk(
                         ctx, emp.Id, ctx.Period.FirstDay, ctx.Period.RangeEnd))
            {
                if (cw > 6)
                {
                    items.Add(new ViolationItem(RuleId, emp.Id, date,
                        $"連續工作計數 {cw} 超過 6（R1 不重置計數）"));
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }
}

/// <summary>GEN-H-03：相鄰實際工作區間休息 ≥ 11 小時（含 X 與歷史最後工作結束時間）。</summary>
public sealed class GenH03RestGap : IRuleEvaluator
{
    public const double MinHours = 11;

    public string RuleId => "GEN-H-03";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            DateTime? lastEnd = ctx.Histories.TryGetValue(emp.Id, out var h) ? h.LastWorkEnd : null;

            for (var d = ctx.Period.FirstDay; d <= ctx.Period.RangeEnd; d = d.AddDays(1))
            {
                foreach (var (start, end) in WorkIntervals(ctx, emp.Id, d))
                {
                    if (lastEnd is { } prev)
                    {
                        var gap = (start - prev).TotalHours;
                        if (gap < MinHours)
                        {
                            items.Add(new ViolationItem(RuleId, emp.Id, d,
                                $"休息僅 {gap:F1} 小時，未滿 11 小時（前次結束 {prev:yyyy-MM-dd HH:mm}）"));
                        }
                    }
                    lastEnd = end;
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }

    public static IEnumerable<(DateTime Start, DateTime End)> WorkIntervals(
        ScheduleContext ctx, string employeeId, DateOnly date)
    {
        var state = ctx.GetState(employeeId, date);
        if (state is null) yield break;

        if (state.Value.Type == DayStateType.X)
        {
            foreach (var x in ctx.XEvents.Where(e => e.EmployeeId == employeeId && e.StartDate == date))
                yield return (x.Start, x.End);
            yield break;
        }

        if (state.Value.IsNormalShift)
            yield return ShiftTimeConfig.Interval(ctx.Unit, date, state.Value.Shift!.Value);
    }
}

/// <summary>GEN-H-04：每 8 週週期一般休假（R＋R*）與 R1 分開額度；含跨月比例保留。</summary>
public sealed class GenH04CycleRest : IRuleEvaluator
{
    public string RuleId => "GEN-H-04";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        var items = new List<ViolationItem>();
        var cycles = CycleResolver.Intersecting(ctx.Cycles, ctx.Period.FirstDay, ctx.Period.RangeEnd);

        foreach (var emp in ctx.Employees)
        {
            foreach (var cycle in cycles)
            {
                EvaluateEmployeeCycle(ctx, emp.Id, cycle, items);
            }
        }
        return RuleResult.From(RuleId, items);
    }

    private static void EvaluateEmployeeCycle(
        ScheduleContext ctx, string empId, CycleInfo cycle, List<ViolationItem> items)
    {
        var rangeEnd = ctx.Period.RangeEnd;
        var monthEnd = ctx.Period.MonthEnd;

        int CountGen(DateOnly upTo) => CountDays(ctx, empId, cycle, upTo, general: true);
        int CountR1(DateOnly upTo) => CountDays(ctx, empId, cycle, upTo, general: false);

        if (cycle.End <= rangeEnd)
        {
            var gen = CountGen(cycle.End);
            var r1 = CountR1(cycle.End);
            if (gen != cycle.RequiredR)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, cycle.End,
                    $"週期一般休假 {gen} ≠ 需求 {cycle.RequiredR}（{cycle.Start:yyyy-MM-dd}～{cycle.End:yyyy-MM-dd}）"));
            }
            if (r1 != cycle.RequiredR1)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, cycle.End,
                    $"週期 R1 {r1} ≠ 需求 {cycle.RequiredR1}（{cycle.Start:yyyy-MM-dd}～{cycle.End:yyyy-MM-dd}）"));
            }
        }
        else
        {
            var gen = CountGen(rangeEnd);
            var r1 = CountR1(rangeEnd);
            if (gen > cycle.RequiredR)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, rangeEnd,
                    $"未結束週期一般休假累積 {gen} 超過 {cycle.RequiredR}"));
            }
            if (r1 > cycle.RequiredR1)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, rangeEnd,
                    $"未結束週期 R1 累積 {r1} 超過 {cycle.RequiredR1}"));
            }

            var remaining = cycle.End.DayNumber - rangeEnd.DayNumber;
            var need = (cycle.RequiredR - gen) + (cycle.RequiredR1 - r1);
            if (need > remaining)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, rangeEnd,
                    $"剩餘 {remaining} 日不足以補足一般休假與 R1（尚需 {need}）"));
            }
        }

        // (c) Proportional reservation across month boundary.
        if (cycle.End > monthEnd)
        {
            var reserved = cycle.ReservedGeneralRest(monthEnd);
            var genToMonth = CountGen(monthEnd);
            var maxAllowed = cycle.RequiredR - reserved;
            if (genToMonth > maxAllowed)
            {
                items.Add(new ViolationItem("GEN-H-04", empId, monthEnd,
                    $"跨月比例保留：截至月底一般休假 {genToMonth} 超過上限 {maxAllowed}（應保留 {reserved}）"));
            }
        }
    }

    private static int CountDays(
        ScheduleContext ctx, string empId, CycleInfo cycle, DateOnly upTo, bool general)
    {
        var n = 0;
        var from = cycle.Start;
        var to = upTo < cycle.End ? upTo : cycle.End;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var state = ctx.GetState(empId, d);
            if (state is null) continue;
            if (general && state.Value.IsGeneralRest) n++;
            if (!general && state.Value.Type == DayStateType.HolidayRest) n++;
        }
        return n;
    }
}

/// <summary>
/// GEN-H-05：Published 歷史與固定事件不可被求解器改寫。
/// 由 Solver（固定常數）與輸入驗證強制；完成班表評估時回傳 Ok。
/// </summary>
public sealed class GenH05FixedHistory : IRuleEvaluator
{
    public string RuleId => "GEN-H-05";

    public RuleResult Evaluate(ScheduleContext ctx) => RuleResult.Ok(RuleId);
}

/// <summary>M-H-01：只能在本站或同群組站工作。</summary>
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

/// <summary>M-H-02：每個班位須由內部人員或合法外派補足（缺班分析除外）。</summary>
public sealed class MH02Coverage : IRuleEvaluator
{
    public string RuleId => "M-H-02";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);

        var items = new List<ViolationItem>();
        var coverage = CoverageCalculator.ComputeM(ctx);
        foreach (var row in coverage)
        {
            if (row.Assigned + row.External < row.Required)
            {
                items.Add(new ViolationItem(RuleId, null, row.Date,
                    $"{row.Location} {row.Shift.ToDisplay()} 缺額：需求 {row.Required}，內部 {row.Assigned}，外派 {row.External}"));
            }
        }
        return RuleResult.From(RuleId, items);
    }
}

/// <summary>M-H-03：僅 LB02／LB04／LB11 可使用外派。</summary>
public sealed class MH03ExternalStations : IRuleEvaluator
{
    public string RuleId => "M-H-03";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.M) return RuleResult.Ok(RuleId);

        var items = new List<ViolationItem>();
        foreach (var row in CoverageCalculator.ComputeM(ctx))
        {
            if (row.External > 0 && !StationConfig.ExternalStations.Contains(row.Location))
            {
                items.Add(new ViolationItem(RuleId, null, row.Date,
                    $"{row.Location} 不可使用外派"));
            }
        }
        return RuleResult.From(RuleId, items);
    }
}

/// <summary>T-H-01：正常班只能是當日固定班別（延伸日：下月資料優先，否則輪轉推算）。</summary>
public sealed class TH01FixedShift : IRuleEvaluator
{
    public string RuleId => "T-H-01";

    public RuleResult Evaluate(ScheduleContext ctx)
    {
        if (ctx.Unit != Unit.T) return RuleResult.Ok(RuleId);

        var items = new List<ViolationItem>();
        foreach (var emp in ctx.Employees)
        {
            foreach (var d in ctx.Period.AllDays)
            {
                var state = ctx.GetState(emp.Id, d);
                if (state?.IsNormalShift != true) continue;

                var expected = ResolveExpectedShift(ctx, emp.Id, d);
                if (expected is null) continue;
                if (state.Value.Shift != expected)
                {
                    items.Add(new ViolationItem(RuleId, emp.Id, d,
                        $"正常班必須為固定班別 {expected.Value.ToDisplay()}，實際為 {state.Value.Shift!.Value.ToDisplay()}"));
                }
            }
        }
        return RuleResult.From(RuleId, items);
    }

    public static ShiftType? ResolveExpectedShift(ScheduleContext ctx, string empId, DateOnly date)
    {
        if (ctx.Period.IsInTargetMonth(date))
        {
            if (ctx.MonthlyShifts is not null && ctx.MonthlyShifts.TryGetValue(empId, out var s))
                return s;
            return null;
        }

        // Extension day: next-month data preferred, else rotate.
        if (ctx.NextMonthShifts is not null && ctx.NextMonthShifts.TryGetValue(empId, out var next))
            return next;
        if (ctx.MonthlyShifts is not null && ctx.MonthlyShifts.TryGetValue(empId, out var cur))
            return cur.NextInRotation();
        return null;
    }
}
