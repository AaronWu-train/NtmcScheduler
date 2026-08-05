using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;

namespace NtmScheduler.Tests.Solvers;

/// <summary>
/// Small-scale fixtures: M ≈ 2 stations / 6 people; T ≈ 1 group / 6 people.
/// </summary>
internal static class SolverFixtures
{
    public static IReadOnlyList<EmployeeInfo> M6() =>
    [
        new("M01", "甲", Unit.M, HomeStation: "LB01"),
        new("M02", "乙", Unit.M, HomeStation: "LB01"),
        new("M03", "丙", Unit.M, HomeStation: "LB01"),
        new("M04", "丁", Unit.M, HomeStation: "LB02"),
        new("M05", "戊", Unit.M, HomeStation: "LB02"),
        new("M06", "己", Unit.M, HomeStation: "LB02"),
    ];

    public static IReadOnlyList<EmployeeInfo> T6(ShiftType shift = ShiftType.Morning) =>
        Enumerable.Range(1, 6).Select(i => new EmployeeInfo(
            $"T{i:D2}", $"檢測{i}", Unit.T,
            Specialty: i <= 2 ? "軌道" : i <= 4 ? "號誌" : null,
            Ability: 3)).ToList();

    /// <summary>
    /// Build a single 56-day cycle covering [historyStart, rangeEnd] with given quotas.
    /// History before FirstDay is filled with a legal rest/work pattern.
    /// </summary>
    public static SolveRequest CreateMRequest(
        YearMonth month,
        int requiredR = 16,
        int requiredR1 = 0,
        IReadOnlyList<SoftRuleSpec>? softRules = null,
        int historyWorkStreak = 0,
        IReadOnlyList<(string Emp, DateOnly Date)>? rStars = null,
        bool sparseStaff = false)
    {
        var period = ScheduleCalendar.CreatePeriod(month);
        var employees = sparseStaff
            ? M6().Take(2).ToList() // too few → shortage
            : M6();

        var cycleStart = period.FirstDay.AddDays(-21);
        var cycleEnd = cycleStart.AddDays(55);
        // Ensure cycle covers period; if period extends past cycleEnd, extend cycle
        if (cycleEnd < period.RangeEnd)
            cycleEnd = period.RangeEnd;
        var cycleDays = cycleEnd.DayNumber - cycleStart.DayNumber + 1;
        var cycles = new List<CycleInfo>
        {
            new(cycleStart, cycleEnd, requiredR, requiredR1)
        };

        var histories = BuildMHistory(employees, cycleStart, period.FirstDay, historyWorkStreak,
            requiredR, requiredR1, period, cycleDays);

        return new SolveRequest
        {
            Unit = Unit.M,
            Period = period,
            Employees = employees,
            Cycles = cycles,
            Histories = histories,
            XEvents = Array.Empty<XEvent>(),
            RStarRequests = rStars ?? Array.Empty<(string, DateOnly)>(),
            SoftRules = softRules ?? Array.Empty<SoftRuleSpec>(),
            Seed = 42,
            TotalTimeLimit = TimeSpan.FromSeconds(60),
            NumSearchWorkers = 1
        };
    }

    public static SolveRequest CreateTRequest(
        YearMonth month,
        ShiftType shift = ShiftType.Morning,
        int requiredR = 16,
        int requiredR1 = 0,
        IReadOnlyList<SoftRuleSpec>? softRules = null)
    {
        var period = ScheduleCalendar.CreatePeriod(month);
        var employees = T6(shift);
        var cycleStart = period.FirstDay.AddDays(-21);
        var cycleEnd = cycleStart.AddDays(55);
        if (cycleEnd < period.RangeEnd)
            cycleEnd = period.RangeEnd;

        var cycles = new List<CycleInfo> { new(cycleStart, cycleEnd, requiredR, requiredR1) };
        var monthly = employees.ToDictionary(e => e.Id, _ => shift);
        var histories = BuildTHistory(employees, cycleStart, period.FirstDay, requiredR, requiredR1, period);

        return new SolveRequest
        {
            Unit = Unit.T,
            Period = period,
            Employees = employees,
            Cycles = cycles,
            Histories = histories,
            XEvents = Array.Empty<XEvent>(),
            MonthlyShifts = monthly,
            NextMonthShifts = employees.ToDictionary(e => e.Id, _ => shift.NextInRotation()),
            SoftRules = softRules ?? Array.Empty<SoftRuleSpec>(),
            Seed = 42,
            TotalTimeLimit = TimeSpan.FromSeconds(60),
            NumSearchWorkers = 1
        };
    }

    /// <summary>
    /// Cycle that ends inside the scheduling range (for AC-15/30/31 equality tests).
    /// </summary>
    public static SolveRequest CreateMRequestCycleEndsInRange(
        YearMonth month,
        int requiredR,
        int requiredR1,
        int historyGeneralRest,
        int historyR1)
    {
        var period = ScheduleCalendar.CreatePeriod(month);
        var employees = M6();
        // Cycle: 14 days history + period up to RangeEnd
        var cycleStart = period.FirstDay.AddDays(-14);
        var cycleEnd = period.RangeEnd; // ends inside range → (a) equalities
        var cycles = new List<CycleInfo> { new(cycleStart, cycleEnd, requiredR, requiredR1) };

        var histories = new Dictionary<string, EmployeeHistory>();
        foreach (var emp in employees)
        {
            var days = new Dictionary<DateOnly, DayState>();
            var genLeft = historyGeneralRest;
            var r1Left = historyR1;
            for (var d = cycleStart; d < period.FirstDay; d = d.AddDays(1))
            {
                if (genLeft > 0)
                {
                    days[d] = DayState.Rest;
                    genLeft--;
                }
                else if (r1Left > 0)
                {
                    days[d] = DayState.HolidayRest;
                    r1Left--;
                }
                else
                {
                    // Rotate shifts at home; insert rest every 6th work day for GEN-H-02
                    var idx = d.DayNumber - cycleStart.DayNumber;
                    if (idx % 7 == 6)
                        days[d] = DayState.Rest;
                    else
                    {
                        var shifts = StationConfig.ShiftsForStation(emp.HomeStation!).ToArray();
                        days[d] = DayState.Work(shifts[idx % shifts.Length], emp.HomeStation);
                    }
                }
            }

            // Recount actual history rests — adjust by forcing remaining quota into history pattern
            histories[emp.Id] = new EmployeeHistory(days, LastWorkEnd: null, OpenBlock: null);
        }

        // Recompute required remaining: for feasibility, set requiredR/R1 based on history + achievable
        // Override: use exact history counts + leave room in period
        return new SolveRequest
        {
            Unit = Unit.M,
            Period = period,
            Employees = employees,
            Cycles = cycles,
            Histories = histories,
            XEvents = Array.Empty<XEvent>(),
            SoftRules = Array.Empty<SoftRuleSpec>(),
            Seed = 7,
            TotalTimeLimit = TimeSpan.FromSeconds(90),
            NumSearchWorkers = 1
        };
    }

    private static Dictionary<string, EmployeeHistory> BuildMHistory(
        IReadOnlyList<EmployeeInfo> employees,
        DateOnly cycleStart,
        DateOnly periodFirst,
        int terminalWorkStreak,
        int requiredR,
        int requiredR1,
        SchedulePeriod period,
        int cycleDays)
    {
        var result = new Dictionary<string, EmployeeHistory>();
        var histDays = periodFirst.DayNumber - cycleStart.DayNumber;
        // Place enough general rests in history so remaining in period is manageable.
        // remaining days in cycle after history ≈ cycleDays - histDays
        var periodDays = period.RangeEnd.DayNumber - periodFirst.DayNumber + 1;
        // Target ~ requiredR * histDays / cycleDays rests in history
        var histRestTarget = Math.Max(0, requiredR - Math.Min(requiredR, periodDays * 2 / 5));

        foreach (var emp in employees)
        {
            var days = new Dictionary<DateOnly, DayState>();
            var rests = 0;
            var r1s = 0;
            var workRun = 0;

            for (var d = cycleStart; d < periodFirst; d = d.AddDays(1))
            {
                var daysLeft = periodFirst.DayNumber - d.DayNumber;
                var forceRest = terminalWorkStreak > 0 && daysLeft <= terminalWorkStreak
                    ? false // fill terminal streak with work
                    : rests < histRestTarget && (workRun >= 5 || (d.DayNumber % 4 == 0 && rests < histRestTarget));

                if (terminalWorkStreak > 0 && daysLeft <= terminalWorkStreak)
                {
                    var shifts = StationConfig.ShiftsForStation(emp.HomeStation!).ToArray();
                    days[d] = DayState.Work(shifts[0], emp.HomeStation);
                    workRun++;
                    continue;
                }

                if (forceRest || workRun >= 6)
                {
                    if (r1s < requiredR1 && d.DayOfWeek == DayOfWeek.Wednesday)
                    {
                        days[d] = DayState.HolidayRest;
                        r1s++;
                    }
                    else
                    {
                        days[d] = DayState.Rest;
                        rests++;
                    }
                    workRun = 0;
                }
                else
                {
                    var shifts = StationConfig.ShiftsForStation(emp.HomeStation!).ToArray();
                    var s = shifts[(d.DayNumber + emp.Id.GetHashCode()) % shifts.Length];
                    // Avoid night→morning gap issues: prefer morning/afternoon alternating
                    if (s == ShiftType.Night && emp.HomeStation == "LB02")
                        s = ShiftType.Morning;
                    days[d] = DayState.Work(s, emp.HomeStation);
                    workRun++;
                }
            }

            DateTime? lastEnd = null;
            for (var d = periodFirst.AddDays(-1); d >= cycleStart; d = d.AddDays(-1))
            {
                if (days.TryGetValue(d, out var st) && st.IsNormalShift)
                {
                    var (_, end) = ShiftTimeConfig.Interval(Unit.M, d, st.Shift!.Value);
                    lastEnd = end;
                    break;
                }
            }

            result[emp.Id] = new EmployeeHistory(days, lastEnd, OpenBlock: null);
        }

        return result;
    }

    private static Dictionary<string, EmployeeHistory> BuildTHistory(
        IReadOnlyList<EmployeeInfo> employees,
        DateOnly cycleStart,
        DateOnly periodFirst,
        int requiredR,
        int requiredR1,
        SchedulePeriod period)
    {
        var result = new Dictionary<string, EmployeeHistory>();
        var histRestTarget = Math.Max(3, requiredR / 3);

        foreach (var emp in employees)
        {
            var days = new Dictionary<DateOnly, DayState>();
            var rests = 0;
            var workRun = 0;
            for (var d = cycleStart; d < periodFirst; d = d.AddDays(1))
            {
                if (workRun >= 5 || (rests < histRestTarget && d.DayNumber % 3 == 0))
                {
                    days[d] = DayState.Rest;
                    rests++;
                    workRun = 0;
                }
                else
                {
                    days[d] = DayState.Work(ShiftType.Morning);
                    workRun++;
                }
            }

            DateTime? lastEnd = null;
            for (var d = periodFirst.AddDays(-1); d >= cycleStart; d = d.AddDays(-1))
            {
                if (days.TryGetValue(d, out var st) && st.IsNormalShift)
                {
                    lastEnd = ShiftTimeConfig.Interval(Unit.T, d, st.Shift!.Value).End;
                    break;
                }
            }

            result[emp.Id] = new EmployeeHistory(days, lastEnd, null);
        }

        return result;
    }

    public static SoftRuleSpec[] Only(params string[] ruleIds) =>
        ruleIds.Select((id, i) => new SoftRuleSpec(id, i + 1, true)).ToArray();
}
