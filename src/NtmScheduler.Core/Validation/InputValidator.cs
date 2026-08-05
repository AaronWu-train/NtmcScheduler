using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Core.Evaluation.Rules.Hard;

namespace NtmScheduler.Core.Validation;

/// <summary>
/// Input payload for INVALID_INPUT checks (docs/03 §6, 14 rules including D-17).
/// </summary>
public sealed class ValidationRequest
{
    public required Unit Unit { get; init; }
    public required SchedulePeriod Period { get; init; }
    public required IReadOnlyList<EmployeeInfo> Employees { get; init; }
    public required IReadOnlyList<CycleInfo> Cycles { get; init; }
    public IReadOnlyList<(string EmployeeId, DateOnly Date)> RStars { get; init; } =
        Array.Empty<(string, DateOnly)>();
    public IReadOnlyList<XEvent> XEvents { get; init; } = Array.Empty<XEvent>();
    public IReadOnlyDictionary<string, EmployeeHistory> Histories { get; init; } =
        new Dictionary<string, EmployeeHistory>();
    public IReadOnlyDictionary<string, ShiftType>? MonthlyShifts { get; init; }
    public IReadOnlyDictionary<(string EmployeeId, DateOnly Date), DayState>? PublishedDays { get; init; }
    public bool HistoryContainsXWithoutEvents { get; init; }
    public IReadOnlyList<string>? CsvFormatErrors { get; init; }
}

public static class InputValidator
{
    public static IReadOnlyList<ValidationError> Validate(ValidationRequest req)
    {
        var errors = new List<ValidationError>();
        CheckCsv(req, errors);
        CheckEmployees(req, errors);
        CheckCycles(req, errors);
        CheckHistory(req, errors);
        CheckEvents(req, errors);
        CheckRStarQuota(req, errors);
        CheckFixedEventP0Conflicts(req, errors); // D-17 / #14
        return errors;
    }

    private static void CheckCsv(ValidationRequest req, List<ValidationError> errors)
    {
        if (req.CsvFormatErrors is null) return;
        foreach (var msg in req.CsvFormatErrors)
            errors.Add(new ValidationError("E13_CSV_FORMAT", msg));
    }

    private static void CheckEmployees(ValidationRequest req, List<ValidationError> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in req.Employees)
        {
            if (!seen.Add(e.Id))
            {
                errors.Add(new ValidationError("E10_EMPLOYEE_ID",
                    $"員工編號重複：{e.Id}", e.Id));
            }

            if (req.Unit == Unit.M)
            {
                if (e.HomeStation is null || !StationConfig.AllStations.Contains(e.HomeStation))
                {
                    errors.Add(new ValidationError("E09_HOME_STATION",
                        $"M 人員 homeStation 必須為 LB01–LB12：{e.Id}", e.Id));
                }
            }
            else
            {
                if (e.Ability is null or < 1 or > 5)
                {
                    errors.Add(new ValidationError("E08_ABILITY",
                        $"T 人員 ability 必須為 1–5 整數：{e.Id}", e.Id));
                }

                if (req.MonthlyShifts is null || !req.MonthlyShifts.ContainsKey(e.Id))
                {
                    errors.Add(new ValidationError("E08_MONTHLY_SHIFT",
                        $"T 人員缺少目標月班組資料：{e.Id}", e.Id));
                }
            }
        }

        var ids = seen;
        foreach (var (empId, date) in req.RStars)
        {
            if (!ids.Contains(empId))
                errors.Add(new ValidationError("E10_UNKNOWN_EMPLOYEE",
                    $"事件引用不存在的員工：{empId}", empId, date));
        }

        foreach (var x in req.XEvents)
        {
            if (!ids.Contains(x.EmployeeId))
                errors.Add(new ValidationError("E10_UNKNOWN_EMPLOYEE",
                    $"事件引用不存在的員工：{x.EmployeeId}", x.EmployeeId, x.StartDate));
        }
    }

    private static void CheckCycles(ValidationRequest req, List<ValidationError> errors)
    {
        var cycles = req.Cycles.OrderBy(c => c.Start).ToList();
        for (var i = 0; i < cycles.Count; i++)
        {
            for (var j = i + 1; j < cycles.Count; j++)
            {
                if (cycles[i].Start <= cycles[j].End && cycles[j].Start <= cycles[i].End)
                {
                    errors.Add(new ValidationError("E12_CYCLE_OVERLAP",
                        $"週期重疊：{cycles[i].Start:yyyy-MM-dd} 與 {cycles[j].Start:yyyy-MM-dd}"));
                }
            }
        }

        foreach (var d in req.Period.AllDays)
        {
            if (CycleResolver.Find(req.Cycles, d) is null)
            {
                errors.Add(new ValidationError("E12_CYCLE_GAP",
                    $"排班區間日期不屬於任何週期：{d:yyyy-MM-dd}", Date: d));
                break;
            }
        }
    }

    private static void CheckHistory(ValidationRequest req, List<ValidationError> errors)
    {
        if (req.HistoryContainsXWithoutEvents)
        {
            errors.Add(new ValidationError("E11_HISTORY_X_EVENTS",
                "歷史含 X 但缺少 events.csv，無法重建完整時間"));
        }

        if (req.Cycles.Count == 0) return;

        DateOnly histRequiredFrom;
        try
        {
            histRequiredFrom = CycleResolver.EarliestIntersectingStart(req.Cycles, req.Period);
        }
        catch (InvalidOperationException)
        {
            return; // already reported as cycle gap
        }

        var histTo = req.Period.FirstDay.AddDays(-1);
        foreach (var emp in req.Employees)
        {
            if (!req.Histories.TryGetValue(emp.Id, out var hist))
            {
                errors.Add(new ValidationError("E11_HISTORY_INSUFFICIENT",
                    $"歷史不足：員工 {emp.Id} 缺少 {histRequiredFrom:yyyy-MM-dd}～{histTo:yyyy-MM-dd}",
                    emp.Id));
                continue;
            }

            for (var d = histRequiredFrom; d <= histTo; d = d.AddDays(1))
            {
                if (!hist.Days.ContainsKey(d))
                {
                    errors.Add(new ValidationError("E11_HISTORY_INSUFFICIENT",
                        $"歷史不足：員工 {emp.Id} 缺少 {d:yyyy-MM-dd}", emp.Id, d));
                    break;
                }
            }
        }
    }

    private static void CheckEvents(ValidationRequest req, List<ValidationError> errors)
    {
        var rStarByEmp = req.RStars.GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Date).ToList());

        // #1 R* and X same day; #5 date in range; #6 published conflict
        foreach (var (empId, date) in req.RStars)
        {
            if (!req.Period.IsInRange(date))
            {
                errors.Add(new ValidationError("E05_OUT_OF_RANGE",
                    $"R* 日期不在排班區間內：{empId} {date:yyyy-MM-dd}", empId, date));
            }

            if (req.XEvents.Any(x => x.EmployeeId == empId && x.StartDate == date))
            {
                errors.Add(new ValidationError("E01_RSTAR_X_SAME_DAY",
                    $"同一人同一日同時有 R* 與 X：{empId} {date:yyyy-MM-dd}", empId, date));
            }

            if (req.PublishedDays is not null
                && req.PublishedDays.ContainsKey((empId, date)))
            {
                errors.Add(new ValidationError("E06_PUBLISHED_CONFLICT",
                    $"新 R* 與已發布日期衝突，需先走人工版本流程：{empId} {date:yyyy-MM-dd}",
                    empId, date));
            }
        }

        // #2 duplicate R*
        foreach (var (empId, dates) in rStarByEmp)
        {
            foreach (var dup in dates.GroupBy(d => d).Where(g => g.Count() > 1))
            {
                errors.Add(new ValidationError("E02_OVERLAP",
                    $"同日重複 R*：{empId} {dup.Key:yyyy-MM-dd}", empId, dup.Key));
            }
        }

        var xByEmp = req.XEvents.GroupBy(x => x.EmployeeId);
        foreach (var group in xByEmp)
        {
            var list = group.OrderBy(x => x.Start).ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var x = list[i];

                // #3 end <= start
                if (x.End <= x.Start)
                {
                    errors.Add(new ValidationError("E03_X_TIME_ORDER",
                        $"X 結束時間不晚於開始時間：{x.EmployeeId}", x.EmployeeId, x.StartDate));
                }

                // #4 spans more than two calendar days
                var startDate = DateOnly.FromDateTime(x.Start);
                var endDate = DateOnly.FromDateTime(x.End);
                // If end is exactly midnight, attribute to previous day.
                if (x.End.TimeOfDay == TimeSpan.Zero && endDate > startDate)
                    endDate = endDate.AddDays(-1);
                if (endDate.DayNumber - startDate.DayNumber > 1)
                {
                    errors.Add(new ValidationError("E04_X_SPAN",
                        $"X 跨超過兩個曆日：{x.EmployeeId} {x.Start:yyyy-MM-dd HH:mm}～{x.End:yyyy-MM-dd HH:mm}",
                        x.EmployeeId, x.StartDate));
                }

                // #5 start in range
                if (!req.Period.IsInRange(x.StartDate))
                {
                    errors.Add(new ValidationError("E05_OUT_OF_RANGE",
                        $"X 開始日期不在排班區間內：{x.EmployeeId} {x.StartDate:yyyy-MM-dd}",
                        x.EmployeeId, x.StartDate));
                }

                // #6 published conflict on X start date
                if (req.PublishedDays is not null
                    && req.PublishedDays.ContainsKey((x.EmployeeId, x.StartDate)))
                {
                    errors.Add(new ValidationError("E06_PUBLISHED_CONFLICT",
                        $"新 X 與已發布日期衝突，需先走人工版本流程：{x.EmployeeId} {x.StartDate:yyyy-MM-dd}",
                        x.EmployeeId, x.StartDate));
                }

                // #2 overlapping X
                for (var j = i + 1; j < list.Count; j++)
                {
                    var y = list[j];
                    if (x.Start < y.End && y.Start < x.End)
                    {
                        errors.Add(new ValidationError("E02_OVERLAP",
                            $"兩筆 X 時間區間重疊：{x.EmployeeId} {x.Start:yyyy-MM-dd HH:mm} 與 {y.Start:yyyy-MM-dd HH:mm}",
                            x.EmployeeId, x.StartDate));
                    }
                }
            }
        }
    }

    private static void CheckRStarQuota(ValidationRequest req, List<ValidationError> errors)
    {
        var cycles = CycleResolver.Intersecting(req.Cycles, req.Period.FirstDay, req.Period.RangeEnd);
        foreach (var emp in req.Employees)
        {
            foreach (var cycle in cycles)
            {
                var histGen = CountHistoryGeneralRest(req, emp.Id, cycle);
                var rStarsInCycle = req.RStars.Count(r =>
                    r.EmployeeId == emp.Id && cycle.Contains(r.Date));
                if (histGen + rStarsInCycle > cycle.RequiredR)
                {
                    errors.Add(new ValidationError("E07_RSTAR_EXCEEDS_REQUIRED_R",
                        $"員工 {emp.Id} 週期 {cycle.Start:yyyy-MM-dd}～{cycle.End:yyyy-MM-dd} 的 R* 過多，使一般休假必然超過 requiredR={cycle.RequiredR}",
                        emp.Id, cycle.Start));
                }

                if (cycle.End > req.Period.MonthEnd)
                {
                    var reserved = cycle.ReservedGeneralRest(req.Period.MonthEnd);
                    var maxAllowed = cycle.RequiredR - reserved;
                    var rStarsToMonth = req.RStars.Count(r =>
                        r.EmployeeId == emp.Id
                        && cycle.Contains(r.Date)
                        && r.Date <= req.Period.MonthEnd);
                    if (histGen + rStarsToMonth > maxAllowed)
                    {
                        errors.Add(new ValidationError("E07_RSTAR_EXCEEDS_RESERVED",
                            $"員工 {emp.Id} 週期 {cycle.Start:yyyy-MM-dd} 截至月底的 R* 過多，超過跨月比例保留上限 {maxAllowed}",
                            emp.Id, cycle.Start));
                    }
                }
            }
        }
    }

    private static int CountHistoryGeneralRest(ValidationRequest req, string empId, CycleInfo cycle)
    {
        if (!req.Histories.TryGetValue(empId, out var hist)) return 0;
        var n = 0;
        var to = req.Period.FirstDay.AddDays(-1);
        for (var d = cycle.Start; d <= cycle.End && d <= to; d = d.AddDays(1))
        {
            if (hist.Days.TryGetValue(d, out var s) && s.IsGeneralRest)
                n++;
        }

        return n;
    }

    /// <summary>
    /// #14 / D-17: fixed X↔X or X↔Published work intervals that necessarily violate GEN-H-03 or GEN-H-02.
    /// </summary>
    private static void CheckFixedEventP0Conflicts(ValidationRequest req, List<ValidationError> errors)
    {
        foreach (var emp in req.Employees)
        {
            var intervals = BuildFixedIntervals(req, emp.Id);
            for (var i = 1; i < intervals.Count; i++)
            {
                var prev = intervals[i - 1];
                var cur = intervals[i];
                var gap = (cur.Start - prev.End).TotalHours;
                if (gap < GenH03RestGap.MinHours)
                {
                    errors.Add(new ValidationError("E14_FIXED_REST_GAP",
                        $"固定事件必然違反 GEN-H-03：{emp.Id} 前次結束 {prev.End:yyyy-MM-dd HH:mm}（{prev.Source}）與下次開始 {cur.Start:yyyy-MM-dd HH:mm}（{cur.Source}）僅間隔 {gap:F1} 小時",
                        emp.Id, cur.Date));
                }
            }

            if (FixedWorkForcesGenH02(req, emp.Id))
            {
                errors.Add(new ValidationError("E14_FIXED_CONTINUOUS_WORK",
                    $"固定事件必然違反 GEN-H-02：{emp.Id} 在已發布歷史與 X 下連續工作計數已超過 6",
                    emp.Id));
            }
        }
    }

    private readonly record struct FixedIv(DateOnly Date, DateTime Start, DateTime End, string Source);

    private static List<FixedIv> BuildFixedIntervals(ValidationRequest req, string empId)
    {
        var list = new List<FixedIv>();

        if (req.Histories.TryGetValue(empId, out var hist))
        {
            foreach (var (d, state) in hist.Days.OrderBy(kv => kv.Key))
            {
                if (state.IsNormalShift)
                {
                    var (s, e) = ShiftTimeConfig.Interval(req.Unit, d, state.Shift!.Value);
                    list.Add(new FixedIv(d, s, e, "Published歷史"));
                }
            }
        }

        if (req.PublishedDays is not null)
        {
            foreach (var ((id, d), state) in req.PublishedDays.Where(kv => kv.Key.EmployeeId == empId))
            {
                if (!state.IsNormalShift) continue;
                if (list.Any(x => x.Date == d)) continue;
                var (s, e) = ShiftTimeConfig.Interval(req.Unit, d, state.Shift!.Value);
                list.Add(new FixedIv(d, s, e, "已發布"));
            }
        }

        foreach (var x in req.XEvents.Where(e => e.EmployeeId == empId))
            list.Add(new FixedIv(x.StartDate, x.Start, x.End, "X事件"));

        return list.OrderBy(i => i.Start).ThenBy(i => i.End).ToList();
    }

    private static bool FixedWorkForcesGenH02(ValidationRequest req, string empId)
    {
        // Walk fixed days only: published history + X start dates as work; published rests as R/R*/R1.
        // Unfixed days break the "necessary" chain — only flag when a contiguous fixed stretch exceeds 6.
        req.Histories.TryGetValue(empId, out var hist);
        var cw = 0;
        DateOnly? start = hist is { Days.Count: > 0 } ? hist.Days.Keys.Min() : req.Period.FirstDay;

        for (var d = start.Value; d <= req.Period.RangeEnd; d = d.AddDays(1))
        {
            DayState? state = null;
            if (hist is not null && hist.Days.TryGetValue(d, out var hs))
                state = hs;
            else if (req.PublishedDays is not null
                     && req.PublishedDays.TryGetValue((empId, d), out var ps))
                state = ps;
            else if (req.XEvents.Any(x => x.EmployeeId == empId && x.StartDate == d))
                state = DayState.X;
            else
            {
                cw = 0;
                continue;
            }

            cw = ContinuousWorkCounter.Compute(state.Value, cw);
            if (cw > 6) return true;
        }

        return false;
    }
}
