using Google.OrTools.Sat;

namespace NtmcScheduler.Solvers;

public static partial class TSolver
{
    // Soft objectives
    // Every measurement is non-negative. Weights compare measurements only inside one priority.
    private static List<ObjectiveGroup> BuildObjectiveGroups(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        IReadOnlyList<DateOnly> modelDates,
        ModelVariables variables,
        SolverOptions options)
    {
        var weights = SolverRuleWeights.Resolve(false, options.RuleWeights);
        List<(string Name, int Weight, LinearExpr Expression)> Group(params (string Name, Func<LinearExpr> Build)[] rules) =>
            rules.Where(x => weights[x.Name] > 0).Select(x => (x.Name, weights[x.Name], x.Build())).ToList();
        static LinearExpr Total(List<(string Name, int Weight, LinearExpr Expression)> components) => LinearExpr.Sum(components.Select(x => x.Expression * x.Weight));
        ObjectiveGroup Objective(int priority, string name, params (string Name, Func<LinearExpr> Build)[] rules)
        {
            var components = Group(rules);
            return new(priority, name, Total(components), components);
        }
        return
        [
            Objective(1, "RequestedRest",
                ("RequestedRest", () => CountUnfulfilledRequestedRests(input, targetDates, variables)),
                ("LeaveRestOutsideRequestedRest", () => CountLeaveRestsOutsideRequestedDates(input, targetDates, variables))),
            Objective(2, "StaffingQuality", ("NonMonthlyShift", () => CountNonMonthlyShiftAssignments(input, modelDates, variables)), ("Attendance", () => MeasureAttendanceShortfall(model, input, targetDates, variables)), ("Specialty", () => CountMissingSpecialties(model, input, targetDates, variables)), ("Ability", () => MeasureAbilityShortfall(model, input, targetDates, variables))),
            Objective(3, "RestDistribution", ("MonthlyRest", () => MeasureMonthlyRestDeviation(model, input, targetDates, variables.Rest)), ("SpecialRestBalance", () => MeasureSpecialRestBalance(model, input, targetDates, variables.SpecialRest))),
            Objective(4, "WorkPatternQuality", ("WorkStreak", () => MeasureWorkStreakPenalties(model, input, targetDates, modelDates, variables)), ("NightToEarlyRest", () => MeasureNightToEarlyRestShortfall(model, input, targetDates, variables)), ("MonthBoundaryRestBalance", () => MeasureMonthBoundaryRestDifference(model, input, targetDates, variables))),
            Objective(5, "RestFairness", ("WeekdayRestFairness", () => MeasureRestCountRangeByMonthlyShift(model, input, targetDates.Where(date => !IsWeekendOrNationalHoliday(input, date)), variables, "weekday_fairness")), ("HolidayRestFairness", () => MeasureHolidayRestPenaltyByMonthlyShift(model, input, targetDates.Where(date => IsWeekendOrNationalHoliday(input, date)), variables)))
        ];
    }

    // 指定休假滿足——計算結果不是實際休假的 R* 格數。
    private static LinearExpr CountUnfulfilledRequestedRests(ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables) =>
        LinearExpr.Sum(from employee in input.DemandMonth.Employees
                       from date in targetDates
                       where employee.Assignments.GetValueOrDefault(date)?.RequestedRest == true
                       select 1 - variables.AnyRest[(employee.EmployeeId, date)]);

    // 非 R* 日期的 R休——與未滿足 R* 依 1:10 權重合併，偏好將 R休 安排在 R*。
    private static LinearExpr CountLeaveRestsOutsideRequestedDates(ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables) =>
        LinearExpr.Sum(from employee in input.DemandMonth.Employees
                       from date in targetDates
                       where employee.Assignments.GetValueOrDefault(date)?.RequestedRest != true
                       select variables.LeaveRest[(employee.EmployeeId, date)]);

    // 月班別一致性——計算不符合目標月班別或延伸日輪轉班別的正常工作格。
    private static LinearExpr CountNonMonthlyShiftAssignments(ScheduleInput input, IReadOnlyList<DateOnly> modelDates, ModelVariables variables) =>
        LinearExpr.Sum(from employee in input.DemandMonth.Employees
                       from date in modelDates
                       from shift in Shifts
                       where shift != MonthlyShiftOnDate(employee, date, input.DemandMonth.MonthStart)
                       select variables.Work[(employee.EmployeeId, date, shift)]);

    // 班組出勤人數——比較月班組目標與該班實際出勤的所有人員。
    private static LinearExpr MeasureAttendanceShortfall(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables)
    {
        var deficits = new List<IntVar>();
        foreach (var date in targetDates)
            foreach (var shift in Shifts)
            {
                var members = input.DemandMonth.Employees.Where(employee => IsEmployedOn(employee, date) && employee.MonthlyShift == shift).ToArray();
                var attendance = LinearExpr.Sum(input.DemandMonth.Employees
                    .Where(employee => IsEmployedOn(employee, date))
                    .Select(employee => variables.Work[(employee.EmployeeId, date, shift)]));
                var deficit = model.NewIntVar(0, members.Length / 2, $"attendance_deficit_{date:yyyyMMdd}_{shift}");
                model.AddMaxEquality(deficit, [members.Length / 2 - attendance, LinearExpr.Constant(0)]);
                deficits.Add(deficit);
            }
        return LinearExpr.Sum(deficits);
    }

    // 專業人員出勤——跨班人員可補足實際工作班組需要的專業。
    private static LinearExpr CountMissingSpecialties(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables)
    {
        var missing = new List<BoolVar>();
        foreach (var date in targetDates)
            foreach (var shift in Shifts)
            {
                var members = input.DemandMonth.Employees.Where(employee => IsEmployedOn(employee, date) && employee.MonthlyShift == shift).ToArray();
                foreach (var specialty in members.Select(employee => employee.Affiliation).Distinct())
                {
                    var specialists = input.DemandMonth.Employees.Where(employee => IsEmployedOn(employee, date) && employee.Affiliation == specialty).ToArray();
                    var attendance = LinearExpr.Sum(specialists.Select(employee => variables.Work[(employee.EmployeeId, date, shift)]));
                    var absent = model.NewBoolVar($"specialty_absent_{date:yyyyMMdd}_{shift}_{specialty}");
                    model.Add(attendance >= 1 - absent);
                    model.Add(attendance <= specialists.Length * (1 - absent));
                    missing.Add(absent);
                }
            }
        return LinearExpr.Sum(missing);
    }

    // 高能力人員配置——每個實際班別希望至少兩位能力 4–5 人員，包含跨班人員。
    private static LinearExpr MeasureAbilityShortfall(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables)
    {
        var deficits = new List<IntVar>();
        foreach (var date in targetDates)
            foreach (var shift in Shifts)
            {
                var highAbilityAttendance = LinearExpr.Sum(input.DemandMonth.Employees
                    .Where(employee => IsEmployedOn(employee, date) && employee.Ability >= 4)
                    .Select(employee => variables.Work[(employee.EmployeeId, date, shift)]));
                var deficit = model.NewIntVar(0, 2, $"ability_deficit_{date:yyyyMMdd}_{shift}");
                model.AddMaxEquality(deficit, [2 - highAbilityAttendance, LinearExpr.Constant(0)]);
                var noHighAbility = model.NewBoolVar($"no_high_ability_{date:yyyyMMdd}_{shift}");
                model.Add(highAbilityAttendance == 0).OnlyEnforceIf(noHighAbility);
                model.Add(highAbilityAttendance >= 1).OnlyEnforceIf(noHighAbility.Not());
                var penalty = model.NewIntVar(0, 10, $"ability_penalty_{date:yyyyMMdd}_{shift}");
                model.Add(penalty == deficit + 8 * noHighAbility);
                deficits.Add(penalty);
            }
        return LinearExpr.Sum(deficits);
    }

    // 每月一般 R 分布——依到職後的週末推導每人的一般 R 目標。
    private static LinearExpr MeasureMonthlyRestDeviation(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        IReadOnlyDictionary<(string Employee, DateOnly Date), BoolVar> rests)
    {
        var penalties = new List<IntVar>();
        foreach (var employee in input.DemandMonth.Employees)
        {
            var activeDates = targetDates.Where(date => IsEmployedOn(employee, date)).ToArray();
            var target = ExpectedMonthlyGeneralRestCount(input, employee);
            var maximum = Math.Max(activeDates.Length, target);
            var count = model.NewIntVar(0, activeDates.Length, $"monthly_rest_count_{employee.EmployeeId}");
            var deviation = model.NewIntVar(0, maximum, $"monthly_rest_deviation_{employee.EmployeeId}");
            var penalty = model.NewIntVar(0, (long)maximum * maximum, $"monthly_rest_penalty_{employee.EmployeeId}");
            model.Add(count == LinearExpr.Sum(activeDates.Select(date => rests[(employee.EmployeeId, date)])));
            model.AddAbsEquality(deviation, count - target);
            model.AddElement(deviation, Enumerable.Range(0, maximum + 1).Select(value => (long)value * value), penalty);
            penalties.Add(penalty);
        }
        return LinearExpr.Sum(penalties);
    }

    // 八週累積 R1 餘額——實際與應有 R1 數量的差額平方計分。
    private static LinearExpr MeasureSpecialRestBalance(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        IReadOnlyDictionary<(string Employee, DateOnly Date), BoolVar> rests)
    {
        var penalties = new List<IntVar>();
        var monthEnd = targetDates[^1];
        foreach (var employee in input.DemandMonth.Employees)
            foreach (var interval in input.RestIntervals.Where(interval => targetDates.Any(date => date >= interval.Start && date <= interval.End)))
            {
                var dates = targetDates.Where(date => date >= interval.Start && date <= interval.End && IsEmployedOn(employee, date)).ToArray();
                var prior = RestUsageBeforeModeledDates(input, employee, interval).SpecialRest;
                var expected = ExpectedSpecialRest(input, employee, interval, monthEnd);
                var values = Enumerable.Range(0, dates.Length + 1).Select(count => SpecialRestBalancePenaltyValue(prior + count, expected)).ToArray();
                var count = model.NewIntVar(0, dates.Length, $"special_rest_balance_count_{employee.EmployeeId}_{interval.Start:yyyyMMdd}");
                var penalty = model.NewIntVar(0, values.Max(), $"special_rest_balance_penalty_{employee.EmployeeId}_{interval.Start:yyyyMMdd}");
                model.Add(count == LinearExpr.Sum(dates.Select(date => rests[(employee.EmployeeId, date)])));
                model.AddElement(count, values, penalty);
                penalties.Add(penalty);
            }
        return LinearExpr.Sum(penalties);
    }

    private static long SpecialRestBalancePenaltyValue(int actual, int expected)
    {
        var deviation = actual - expected;
        return (long)deviation * deviation;
    }

    private static int ExpectedSpecialRest(ScheduleInput input, EmployeeMonthlySchedule employee, RestInterval interval, DateOnly monthEnd)
    {
        var monthStart = input.DemandMonth.MonthStart;
        var priorTarget = interval.NationalHolidays.Count(date => date < monthStart);
        var start = employee.EmploymentStartDate is { } hired && hired > monthStart ? hired : monthStart;
        monthEnd = employee.EmploymentEndDate is { } ended && ended < monthEnd ? ended : monthEnd;
        priorTarget += interval.NationalHolidays.Count(date => date >= monthStart && date < start);
        var employeeTarget = Math.Max(0, input.MonthlySettings!.SpecialRestTarget -
            input.RestIntervals.SelectMany(x => x.NationalHolidays).Count(date => date >= monthStart && date < start));
        var intersecting = input.RestIntervals.Where(x => x.Start <= monthEnd && x.End >= start).OrderBy(x => x.Start).ToArray();
        var weights = intersecting.Select(x => x.NationalHolidays.Count(date => date >= start && date <= monthEnd)).ToArray();
        if (weights.Sum() == 0) weights = intersecting.Select(x => Math.Max(0, Math.Min(x.End.DayNumber, monthEnd.DayNumber) - Math.Max(x.Start.DayNumber, start.DayNumber) + 1)).ToArray();
        var total = Math.Max(1, weights.Sum());
        var allocations = weights.Select(weight => employeeTarget * weight / total).ToArray();
        var remainder = employeeTarget - allocations.Sum();
        var index = 0;
        for (; remainder > 0; remainder--, index = (index + 1) % allocations.Length) allocations[index]++;
        var intervalIndex = Array.IndexOf(intersecting, interval);
        return priorTarget + (intervalIndex < 0 ? 0 : allocations[intervalIndex]);
    }

    // 連續工作區段——計算已結束的實際工作區段；R、R1、R休會結束區段。
    private static LinearExpr MeasureWorkStreakPenalties(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        IReadOnlyList<DateOnly> modelDates,
        ModelVariables variables)
    {
        var target = targetDates.ToHashSet();
        var penalties = new List<IntVar>();
        foreach (var employee in input.DemandMonth.Employees)
        {
            LinearExpr previousCount = LinearExpr.Constant(HistoricalWorkStreakLength(input, employee.EmployeeId));
            for (var index = 0; index < modelDates.Count; index++)
            {
                var date = modelDates[index];
                var work = variables.ActualWork[(employee.EmployeeId, date)];
                var count = model.NewIntVar(0, modelDates.Count + 31, $"work_streak_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.Add(count == previousCount + 1).OnlyEnforceIf(work);
                model.Add(count == 0).OnlyEnforceIf(work.Not());
                if (target.Contains(date) && index + 1 < modelDates.Count)
                {
                    var nextWork = variables.ActualWork[(employee.EmployeeId, modelDates[index + 1])];
                    var streakEnds = model.NewBoolVar($"work_streak_ends_{employee.EmployeeId}_{date:yyyyMMdd}");
                    model.Add(streakEnds <= work);
                    model.Add(streakEnds + nextWork <= 1);
                    model.Add(streakEnds >= work - nextWork);

                    var maximumLength = modelDates.Count + 31;
                    var rawPenalty = model.NewIntVar(0, 2L * maximumLength, $"work_streak_penalty_{employee.EmployeeId}_{date:yyyyMMdd}_raw");
                    var penalty = model.NewIntVar(0, 2L * maximumLength, $"work_streak_penalty_{employee.EmployeeId}_{date:yyyyMMdd}");
                    model.AddElement(count, Enumerable.Range(0, maximumLength + 1).Select(BlockLengthPenaltyValue), rawPenalty);
                    model.AddMultiplicationEquality(penalty, rawPenalty, streakEnds);
                    penalties.Add(penalty);
                }
                previousCount = count;
            }
        }
        return LinearExpr.Sum(penalties);
    }

    // 跨月夜轉早休假——從歷史最後一個實際夜班計算休假；沒有夜班就不懲罰。
    private static LinearExpr MeasureNightToEarlyRestShortfall(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables)
    {
        var deficits = new List<IntVar>();
        foreach (var employee in input.DemandMonth.Employees.Where(employee => employee.MonthlyShift == Shift.Early))
        {
            var history = ResolvedHistoryFor(input, employee.EmployeeId).OrderBy(item => item.Date).ToArray();
            var lastNight = history.LastOrDefault(item => item.Cell.Kind == AssignmentKind.Work && item.Cell.Shift == Shift.Night);
            if (lastNight.Cell is null) continue;
            var historicalRest = history.Count(item => item.Date > lastNight.Date && item.Cell.Kind is AssignmentKind.Rest or AssignmentKind.SpecialRest or AssignmentKind.LeaveRest);
            var previousWork = new List<BoolVar>();
            foreach (var date in targetDates.Where(date => IsEmployedOn(employee, date)))
            {
                var work = variables.Work[(employee.EmployeeId, date, Shift.Early)];
                var first = model.NewBoolVar($"first_early_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.Add(first <= work);
                foreach (var previous in previousWork) model.Add(first + previous <= 1);
                model.Add(first >= work - LinearExpr.Sum(previousWork));

                var restBefore = historicalRest + LinearExpr.Sum(targetDates.Where(value => value < date && IsEmployedOn(employee, value)).Select(value => variables.AnyRest[(employee.EmployeeId, value)]));
                var raw = model.NewIntVar(0, 2, $"night_early_raw_{employee.EmployeeId}_{date:yyyyMMdd}");
                var deficit = model.NewIntVar(0, 2, $"night_early_deficit_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.AddMaxEquality(raw, [2 - restBefore, LinearExpr.Constant(0)]);
                model.AddMultiplicationEquality(deficit, raw, first);
                deficits.Add(deficit);
                previousWork.Add(work);
            }
        }
        return LinearExpr.Sum(deficits);
    }

    // 月交界休假平衡——比較實際夜轉早前後兩個月交界日的休假人數。
    private static LinearExpr MeasureMonthBoundaryRestDifference(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables)
    {
        var transitioning = input.DemandMonth.Employees.Where(employee =>
            employee.MonthlyShift == Shift.Early &&
            ResolvedHistoryFor(input, employee.EmployeeId).Any(item => item.Cell.Kind == AssignmentKind.Work && item.Cell.Shift == Shift.Night)).ToArray();
        if (transitioning.Length == 0) return LinearExpr.Constant(0);
        var previousDate = input.DemandMonth.MonthStart.AddDays(-1);
        var previousRest = transitioning.Count(employee =>
            ResolvedHistoryFor(input, employee.EmployeeId).FirstOrDefault(item => item.Date == previousDate).Cell?.Kind is AssignmentKind.Rest or AssignmentKind.SpecialRest or AssignmentKind.LeaveRest);
        var currentRest = LinearExpr.Sum(transitioning.Select(employee => variables.AnyRest[(employee.EmployeeId, targetDates[0])]));
        var difference = model.NewIntVar(0, transitioning.Length, "month_boundary_rest_balance");
        model.AddAbsEquality(difference, previousRest - currentRest);
        return difference;
    }

    // 休假公平——只比較同一 T 月班別內全月在職的人員。
    private static LinearExpr MeasureRestCountRangeByMonthlyShift(
        CpModel model,
        ScheduleInput input,
        IEnumerable<DateOnly> dates,
        ModelVariables variables,
        string name)
    {
        var selectedDates = dates.ToArray();
        var ranges = new List<LinearExpr>();
        foreach (var group in input.DemandMonth.Employees
                     .Where(employee => IsEmployedOn(employee, input.DemandMonth.MonthStart))
                     .GroupBy(employee => employee.MonthlyShift))
        {
            var counts = group.Select(employee =>
            {
                var count = model.NewIntVar(0, selectedDates.Length, $"{name}_{employee.EmployeeId}");
                model.Add(count == LinearExpr.Sum(selectedDates.Select(date => variables.AnyRest[(employee.EmployeeId, date)])));
                return (LinearExpr)count;
            }).ToArray();
            if (counts.Length < 2) continue;
            var maximum = model.NewIntVar(0, selectedDates.Length, $"{name}_max_{group.Key}");
            var minimum = model.NewIntVar(0, selectedDates.Length, $"{name}_min_{group.Key}");
            model.AddMaxEquality(maximum, counts);
            model.AddMinEquality(minimum, counts);
            ranges.Add(maximum - minimum);
        }
        return LinearExpr.Sum(ranges);
    }

    // 假日休假公平——同一月班別內以平均正負 1.5 天為免罰區間。
    private static LinearExpr MeasureHolidayRestPenaltyByMonthlyShift(
        CpModel model,
        ScheduleInput input,
        IEnumerable<DateOnly> dates,
        ModelVariables variables)
    {
        var selectedDates = dates.ToArray();
        var penalties = new List<LinearExpr>();
        foreach (var group in input.DemandMonth.Employees
                     .Where(employee => IsEmployedOn(employee, input.DemandMonth.MonthStart))
                     .GroupBy(employee => employee.MonthlyShift))
        {
            var counts = group.Select(employee =>
            {
                var count = model.NewIntVar(0, selectedDates.Length, $"holiday_fairness_{employee.EmployeeId}");
                model.Add(count == LinearExpr.Sum(selectedDates.Select(date => variables.AnyRest[(employee.EmployeeId, date)])));
                return count;
            }).ToArray();
            if (counts.Length < 2) continue;
            var groupSize = counts.Length;
            var total = model.NewIntVar(
                0, (long)groupSize * selectedDates.Length, $"holiday_fairness_{group.Key}_total");
            model.Add(total == LinearExpr.Sum(counts));
            for (var index = 0; index < counts.Length; index++)
            {
                // 2 * |n*c - T| - 3n keeps the exact fractional average without floating point.
                var distance = model.NewIntVar(
                    0, (long)groupSize * selectedDates.Length, $"holiday_fairness_{group.Key}_distance_{index}");
                model.AddAbsEquality(distance, counts[index] * groupSize - total);
                var excess = model.NewIntVar(
                    0, 2L * groupSize * selectedDates.Length, $"holiday_fairness_{group.Key}_excess_{index}");
                model.AddMaxEquality(excess, [distance * 2 - 3 * groupSize, LinearExpr.Constant(0)]);
                var penalty = model.NewIntVar(
                    0, selectedDates.Length, $"holiday_fairness_{group.Key}_penalty_{index}");
                model.AddDivisionEquality(penalty, excess + 2 * groupSize - 1, 2 * groupSize);
                penalties.Add(penalty);
            }
        }
        return LinearExpr.Sum(penalties);
    }

    private static bool IsWeekendOrNationalHoliday(ScheduleInput input, DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
        || input.RestIntervals.Any(interval => interval.NationalHolidays.Contains(date));

    // Piecewise penalty for completed work-streak lengths.
    private static int BlockLengthPenaltyValue(int length) => length switch
    {
        0 => 0,
        1 => 4,
        2 => 1,
        3 => 0,
        4 => 0,
        5 => 1,
        _ when length >= 6 => 2 * (length - 4),
        _ => 0
    };

    private sealed record ObjectiveGroup(
        int Priority,
        string Name,
        LinearExpr Total,
        List<(string Name, int Weight, LinearExpr Expression)> Components);
}
