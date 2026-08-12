using Google.OrTools.Sat;

namespace NtmScheduler.Solvers;

public static partial class MSolver
{
    // Soft objectives
    // Every measurement is non-negative. Weights compare measurements only inside one priority.
    private static List<ObjectiveGroup> BuildObjectiveGroups(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        IReadOnlyList<DateOnly> modelDates,
        ModelVariables variables)
    {
        const int objectiveScale = 10; // Scales ordinary violations; fairness weights use their documented output units.
        var requestedRest = CountUnfulfilledRequestedRests(input, targetDates, variables);
        var unusedLeaveRest = MeasureUnusedLeaveRests(model, input, targetDates, variables);
        var externalStaffing = MeasureExternalStaffingAboveAllowance(model, targetDates, variables);
        var monthlyRest = MeasureMonthlyRestDeviation(model, input, targetDates, variables.Rest);
        var specialRestBalance = MeasureSpecialRestBalance(model, input, targetDates, variables.SpecialRest);
        var workStreak = MeasureWorkStreakPenalties(model, input, targetDates, modelDates, variables);
        var mixedShiftWorkStreak = CountMixedShiftWorkStreaks(model, input, targetDates, modelDates, variables);
        var nightRestEarly = CountNightRestShiftPatterns(model, input, variables, Shift.Early, "night_rest_early");
        var nightRestAfternoon = CountNightRestShiftPatterns(model, input, variables, Shift.Afternoon, "night_rest_afternoon");
        var shiftChangeWithoutRest = CountShiftChangesWithoutRest(model, input, modelDates, variables);
        var holidayFairness = MeasureRestCountDeviationByStationGroup(model, input, targetDates.Where(date => IsWeekendOrNationalHoliday(input, date)), variables, "holiday_fairness");
        var earlyAfternoonImbalance = MeasureEarlyAfternoonImbalance(model, input, targetDates, variables);
        var nightShiftTarget = MeasureNightShiftTargetPenalty(model, input, targetDates, variables);

        return
        [
            new(1, "RequestedRest", requestedRest * 3 + unusedLeaveRest,
                [("RequestedRest", 3, requestedRest), ("UnusedLeaveRest", 1, unusedLeaveRest)]),
            new(4, "ScheduleQualityAndFairness",
                externalStaffing * (10 * objectiveScale)
                    + monthlyRest * (24 * objectiveScale)
                    + specialRestBalance * (12 * objectiveScale)
                    + workStreak * (4 * objectiveScale)
                    + mixedShiftWorkStreak * (3 * objectiveScale)
                    + nightRestEarly * (40 * objectiveScale)
                    + nightRestAfternoon * (30 * objectiveScale)
                    + shiftChangeWithoutRest * 5
                    + holidayFairness * 5
                    + earlyAfternoonImbalance * 2
                    + nightShiftTarget * 50,
                [
                    ("ExternalStaffing", 10 * objectiveScale, externalStaffing),
                    ("MonthlyRest", 24 * objectiveScale, monthlyRest),
                    ("SpecialRestBalance", 12 * objectiveScale, specialRestBalance),
                    ("WorkStreak", 4 * objectiveScale, workStreak),
                    ("MixedShiftWorkStreak", 3 * objectiveScale, mixedShiftWorkStreak),
                    ("NightRestEarly", 40 * objectiveScale, nightRestEarly),
                    ("NightRestAfternoon", 30 * objectiveScale, nightRestAfternoon),
                    ("ShiftChangeWithoutRest", 5, shiftChangeWithoutRest),
                    ("HolidayRestFairness", 5, holidayFairness),
                    ("EarlyAfternoonImbalance", 2, earlyAfternoonImbalance),
                    ("NightShiftTarget", 50, nightShiftTarget)
                ])
        ];
    }

    // 指定休假滿足——計算結果不是實際休假的 R* 格數。
    private static LinearExpr CountUnfulfilledRequestedRests(ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables) =>
        LinearExpr.Sum(from employee in input.DemandMonth.Employees
            from date in targetDates
            where employee.Assignments.GetValueOrDefault(date)?.RequestedRest == true
            select 1 - variables.AnyRest[(employee.EmployeeId, date)]);

    // 未使用 R休 額度——計算每人上限與目標月實際使用數的差額。
    private static LinearExpr MeasureUnusedLeaveRests(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables)
    {
        var unused = new List<IntVar>();
        foreach (var employee in input.DemandMonth.Employees)
        {
            var limit = employee.RequestedLeaveRestCount ?? 0;
            var value = model.NewIntVar(0, limit, $"unused_leave_rest_{employee.EmployeeId}");
            model.Add(LinearExpr.Sum(targetDates.Select(date => variables.LeaveRest[(employee.EmployeeId, date)])) + value == limit);
            unused.Add(value);
        }
        return LinearExpr.Sum(unused);
    }

    // 外援人力——原三站前 70 人次免罰；LB09 前 4 人次免罰。
    private static LinearExpr MeasureExternalStaffingAboveAllowance(CpModel model, IReadOnlyList<DateOnly> targetDates, ModelVariables variables)
    {
        var target = variables.External.Where(x => targetDates.Contains(x.Key.Date)).ToArray();
        var legacy = target.Where(x => x.Key.Station != "LB09").Select(x => x.Value).ToArray();
        var lb09 = target.Where(x => x.Key.Station == "LB09").Select(x => x.Value).ToArray();
        var legacyAboveAllowance = model.NewIntVar(0, legacy.Length, "external_staffing_above_allowance");
        var lb09AboveAllowance = model.NewIntVar(0, lb09.Length, "lb09_external_staffing_above_allowance");
        model.AddMaxEquality(legacyAboveAllowance, [LinearExpr.Sum(legacy) - 70, LinearExpr.Constant(0)]);
        model.AddMaxEquality(lb09AboveAllowance, [LinearExpr.Sum(lb09) - 4, LinearExpr.Constant(0)]);
        return legacyAboveAllowance + lb09AboveAllowance;
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

    // 八週累積 R1 餘額——每個區間可暫欠一日；超額與其餘欠額平方計分。
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
            var expected = interval.NationalHolidays.Count(date => date <= (interval.End < monthEnd ? interval.End : monthEnd));
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
        var balance = actual - expected;
        var penalizedDeviation = balance > 0 ? balance : Math.Max(0, -balance - 1);
        return (long)penalizedDeviation * penalizedDeviation;
    }

    // Disabled at BuildObjectiveGroups while its weight is zero.
    private static LinearExpr CountCrossStationAssignments(ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables) =>
        LinearExpr.Sum(from employee in input.DemandMonth.Employees
            from date in targetDates
            select variables.SupportsOtherStation[(employee.EmployeeId, date)]);

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
                    model.AddElement(count, Enumerable.Range(0, maximumLength + 1).Select(WorkStreakPenaltyValue), rawPenalty);
                    model.AddMultiplicationEquality(penalty, rawPenalty, streakEnds);
                    penalties.Add(penalty);
                }
                previousCount = count;
            }
        }
        return LinearExpr.Sum(penalties);
    }

    // 工作區段班型一致性——每個已結束的實際工作區段若包含多種正常班型，計一次違反。
    private static LinearExpr CountMixedShiftWorkStreaks(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        IReadOnlyList<DateOnly> modelDates,
        ModelVariables variables)
    {
        var target = targetDates.ToHashSet();
        var penalties = new List<BoolVar>();
        foreach (var employee in input.DemandMonth.Employees)
        {
            var history = HistoricalWorkStreakShiftState(input, employee.EmployeeId);
            LinearExpr previousShift = LinearExpr.Constant(ShiftStateCode(history.LastShift));
            LinearExpr previousMixed = LinearExpr.Constant(history.Mixed ? 1 : 0);
            for (var index = 0; index < modelDates.Count; index++)
            {
                var date = modelDates[index];
                var hasNormal = model.NewBoolVar($"streak_shift_has_work_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.Add(hasNormal == LinearExpr.Sum(Shifts.Select(shift => variables.WorksShift[(employee.EmployeeId, date, shift)])));
                var lastShift = model.NewIntVar(0, 3, $"streak_shift_last_{employee.EmployeeId}_{date:yyyyMMdd}");
                var mixed = model.NewBoolVar($"streak_shift_mixed_{employee.EmployeeId}_{date:yyyyMMdd}");

                if (employee.Assignments.GetValueOrDefault(date)?.Kind == AssignmentKind.WorkEvent)
                {
                    model.Add(lastShift == previousShift);
                    model.Add(mixed == previousMixed);
                }
                else
                {
                    model.Add(lastShift == 0).OnlyEnforceIf(hasNormal.Not());
                    model.Add(mixed == 0).OnlyEnforceIf(hasNormal.Not());

                    foreach (var shift in Shifts)
                    {
                        var normal = variables.WorksShift[(employee.EmployeeId, date, shift)];
                        var same = model.NewBoolVar($"streak_shift_same_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                        model.Add(previousShift == ShiftStateCode(shift)).OnlyEnforceIf(same);
                        model.Add(previousShift != ShiftStateCode(shift)).OnlyEnforceIf(same.Not());
                        var none = model.NewBoolVar($"streak_shift_none_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                        model.Add(previousShift == 0).OnlyEnforceIf(none);
                        model.Add(previousShift != 0).OnlyEnforceIf(none.Not());
                        var changed = model.NewBoolVar($"streak_shift_changed_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                        model.Add(changed <= normal);
                        model.Add(changed + same <= 1);
                        model.Add(changed + none <= 1);
                        model.Add(changed >= normal - same - none);
                        model.Add(lastShift == ShiftStateCode(shift)).OnlyEnforceIf(normal);
                        model.Add(mixed >= changed);
                        model.Add(mixed >= previousMixed + normal - 1);
                        model.Add(mixed <= previousMixed + changed).OnlyEnforceIf(normal);
                    }
                }

                if (target.Contains(date) && index + 1 < modelDates.Count)
                {
                    var nextWork = variables.ActualWork[(employee.EmployeeId, modelDates[index + 1])];
                    var penalty = model.NewBoolVar($"mixed_shift_streak_{employee.EmployeeId}_{date:yyyyMMdd}");
                    model.Add(penalty <= mixed);
                    model.Add(penalty + nextWork <= 1);
                    model.Add(penalty >= mixed - nextWork);
                    penalties.Add(penalty);
                }
                previousShift = lastShift;
                previousMixed = mixed;
            }
        }
        return LinearExpr.Sum(penalties);
    }

    // 夜班--休假--早／午班型態——檢查所有與目標月相交的三日視窗。
    private static LinearExpr CountNightRestShiftPatterns(
        CpModel model,
        ScheduleInput input,
        ModelVariables variables,
        Shift finalShift,
        string name)
    {
        var patterns = new List<BoolVar>();
        var first = input.DemandMonth.MonthStart.AddDays(-2);
        var last = input.DemandMonth.MonthStart.AddMonths(1).AddDays(-1);
        foreach (var employee in input.DemandMonth.Employees)
        {
            for (var date = first; date <= last; date = date.AddDays(1))
            {
                var night = WorkShiftIndicator(input, variables, employee.EmployeeId, date, Shift.Night);
                var rest = RestIndicator(input, variables, employee.EmployeeId, date.AddDays(1));
                var nextShift = WorkShiftIndicator(input, variables, employee.EmployeeId, date.AddDays(2), finalShift);
                var pattern = model.NewBoolVar($"{name}_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.Add(pattern <= night);
                model.Add(pattern <= rest);
                model.Add(pattern <= nextShift);
                model.Add(pattern >= night + rest + nextShift - 2);
                patterns.Add(pattern);
            }
        }
        return LinearExpr.Sum(patterns);
    }

    // 未休假直接換班——比較正常班並略過 X，只計入目標月發生的換班。
    private static LinearExpr CountShiftChangesWithoutRest(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> modelDates,
        ModelVariables variables)
    {
        var violations = new List<BoolVar>();
        var targetEnd = input.DemandMonth.MonthStart.AddMonths(1).AddDays(-1);
        foreach (var employee in input.DemandMonth.Employees)
        {
            LinearExpr previous = LinearExpr.Constant(ShiftStateCode(HistoricalLastShiftSinceRest(input, employee.EmployeeId)));
            foreach (var date in modelDates)
            {
                var previousByShift = new Dictionary<Shift, BoolVar>();
                foreach (var shift in Shifts)
                {
                    var wasShift = model.NewBoolVar($"previous_since_rest_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                    model.Add(previous == ShiftStateCode(shift)).OnlyEnforceIf(wasShift);
                    model.Add(previous != ShiftStateCode(shift)).OnlyEnforceIf(wasShift.Not());
                    previousByShift[shift] = wasShift;
                }
                if (date <= targetEnd)
                {
                    foreach (var current in Shifts)
                        foreach (var prior in Shifts.Where(prior => prior != current))
                        {
                            var currentWork = variables.WorksShift[(employee.EmployeeId, date, current)];
                            var violation = model.NewBoolVar($"shift_without_rest_{employee.EmployeeId}_{date:yyyyMMdd}_{prior}_{current}");
                            model.Add(violation <= currentWork);
                            model.Add(violation <= previousByShift[prior]);
                            model.Add(violation >= currentWork + previousByShift[prior] - 1);
                            violations.Add(violation);
                        }
                }

                var state = model.NewIntVar(0, 3, $"last_since_rest_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.Add(state == 0).OnlyEnforceIf(variables.AnyRest[(employee.EmployeeId, date)]);
                foreach (var shift in Shifts)
                    model.Add(state == ShiftStateCode(shift)).OnlyEnforceIf(variables.WorksShift[(employee.EmployeeId, date, shift)]);
                if (!IsEmployedOn(employee, date) || employee.Assignments.GetValueOrDefault(date)?.Kind == AssignmentKind.WorkEvent)
                    model.Add(state == previous);
                previous = state;
            }
        }
        return LinearExpr.Sum(violations);
    }

    // Disabled at BuildObjectiveGroups while its weight is zero.
    private static LinearExpr CountNonPreferredRotations(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> modelDates,
        ModelVariables variables)
    {
        var preferred = new HashSet<(Shift From, Shift To)>
        {
            (Shift.Early, Shift.Afternoon),
            (Shift.Afternoon, Shift.Night),
            (Shift.Night, Shift.Early)
        };
        var targetEnd = input.DemandMonth.MonthStart.AddMonths(1).AddDays(-1);
        var violations = new List<BoolVar>();

        foreach (var employee in input.DemandMonth.Employees)
        {
            LinearExpr previous = LinearExpr.Constant(ShiftStateCode(HistoricalLastNormalShift(input, employee.EmployeeId)));
            foreach (var date in modelDates)
            {
                var hasNormal = model.NewBoolVar($"rotation_has_normal_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.Add(hasNormal == LinearExpr.Sum(Shifts.Select(shift => variables.WorksShift[(employee.EmployeeId, date, shift)])));
                var previousByShift = new Dictionary<Shift, BoolVar>();
                foreach (var shift in Shifts)
                {
                    var wasShift = model.NewBoolVar($"rotation_previous_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                    model.Add(previous == ShiftStateCode(shift)).OnlyEnforceIf(wasShift);
                    model.Add(previous != ShiftStateCode(shift)).OnlyEnforceIf(wasShift.Not());
                    previousByShift[shift] = wasShift;
                }

                if (date <= targetEnd)
                {
                    foreach (var current in Shifts)
                    foreach (var prior in Shifts.Where(prior => prior != current && !preferred.Contains((prior, current))))
                    {
                        var currentWork = variables.WorksShift[(employee.EmployeeId, date, current)];
                        var violation = model.NewBoolVar($"nonpreferred_rotation_{employee.EmployeeId}_{date:yyyyMMdd}_{prior}_{current}");
                        model.Add(violation <= currentWork);
                        model.Add(violation <= previousByShift[prior]);
                        model.Add(violation >= currentWork + previousByShift[prior] - 1);
                        violations.Add(violation);
                    }
                }

                var state = model.NewIntVar(0, 3, $"rotation_last_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.Add(state == previous).OnlyEnforceIf(hasNormal.Not());
                foreach (var shift in Shifts)
                    model.Add(state == ShiftStateCode(shift)).OnlyEnforceIf(variables.WorksShift[(employee.EmployeeId, date, shift)]);
                previous = state;
            }
        }
        return LinearExpr.Sum(violations);
    }

    // 休假公平——只比較同站群組且目標月全月在職的人員。
    private static LinearExpr MeasureRestCountDeviationByStationGroup(
        CpModel model,
        ScheduleInput input,
        IEnumerable<DateOnly> dates,
        ModelVariables variables,
        string name)
    {
        var selectedDates = dates.ToArray();
        var deviations = new List<LinearExpr>();
        foreach (var group in input.DemandMonth.Employees
                     .Where(employee => IsEmployedOn(employee, input.DemandMonth.MonthStart))
                     .GroupBy(employee => StationGroupIndex(employee.Affiliation)))
        {
            var counts = group.Select(employee =>
            {
                var count = model.NewIntVar(0, selectedDates.Length, $"{name}_{employee.EmployeeId}");
                model.Add(count == LinearExpr.Sum(selectedDates.Select(date => variables.AnyRest[(employee.EmployeeId, date)])));
                return count;
            }).ToArray();
            if (counts.Length < 2) continue;
            deviations.Add(MeasureNormalizedCountDeviationTenths(model, counts, selectedDates.Length, $"{name}_{group.Key}"));
        }
        return LinearExpr.Sum(deviations);
    }

    // Disabled at BuildObjectiveGroups while its weight is zero.
    private static LinearExpr MeasureSupportCountDeviationByStationGroup(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        ModelVariables variables)
    {
        var deviations = new List<LinearExpr>();
        foreach (var group in input.DemandMonth.Employees
                     .Where(employee => IsEmployedOn(employee, input.DemandMonth.MonthStart))
                     .GroupBy(employee => StationGroupIndex(employee.Affiliation)))
        {
            var counts = group.Select(employee =>
            {
                var count = model.NewIntVar(0, targetDates.Count, $"support_fairness_{employee.EmployeeId}");
                model.Add(count == LinearExpr.Sum(targetDates.Select(date => variables.SupportsOtherStation[(employee.EmployeeId, date)])));
                return count;
            }).ToArray();
            if (counts.Length < 2) continue;
            deviations.Add(MeasureNormalizedCountDeviationTenths(model, counts, targetDates.Count, $"support_fairness_{group.Key}"));
        }
        return LinearExpr.Sum(deviations);
    }

    // 早小班差距——每人差距超過四班的部分才計罰。
    private static LinearExpr MeasureEarlyAfternoonImbalance(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        ModelVariables variables)
    {
        var penalties = new List<IntVar>();
        foreach (var employee in input.DemandMonth.Employees.Where(employee => IsEmployedOn(employee, input.DemandMonth.MonthStart)))
        {
            var difference = model.NewIntVar(0, targetDates.Count, $"early_afternoon_difference_{employee.EmployeeId}");
            var penalty = model.NewIntVar(0, targetDates.Count, $"early_afternoon_imbalance_{employee.EmployeeId}");
            var early = LinearExpr.Sum(targetDates.Select(date => variables.WorksShift[(employee.EmployeeId, date, Shift.Early)]));
            var afternoon = LinearExpr.Sum(targetDates.Select(date => variables.WorksShift[(employee.EmployeeId, date, Shift.Afternoon)]));
            model.AddAbsEquality(difference, early - afternoon);
            model.AddMaxEquality(penalty, [difference - 4, LinearExpr.Constant(0)]);
            penalties.Add(penalty);
        }
        return LinearExpr.Sum(penalties);
    }

    // 夜班目標——三、四班不罰，其餘班數依指定函數計罰。
    private static LinearExpr MeasureNightShiftTargetPenalty(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        ModelVariables variables)
    {
        var penalties = new List<IntVar>();
        var values = Enumerable.Range(0, targetDates.Count + 1).Select(NightShiftPenaltyValue).ToArray();
        foreach (var employee in input.DemandMonth.Employees.Where(employee => IsEmployedOn(employee, input.DemandMonth.MonthStart)))
        {
            var count = model.NewIntVar(0, targetDates.Count, $"night_shift_count_{employee.EmployeeId}");
            var penalty = model.NewIntVar(0, values.Max(), $"night_shift_target_{employee.EmployeeId}");
            model.Add(count == LinearExpr.Sum(targetDates.Select(date => variables.WorksShift[(employee.EmployeeId, date, Shift.Night)])));
            model.AddElement(count, values, penalty);
            penalties.Add(penalty);
        }
        return LinearExpr.Sum(penalties);
    }

    private static long NightShiftPenaltyValue(int count) => count switch
    {
        0 => 10,
        1 => 5,
        2 => 1,
        3 or 4 => 0,
        _ => 4L * (count - 4)
    };

    private static IntVar MeasureNormalizedCountDeviationTenths(
        CpModel model,
        IReadOnlyList<IntVar> counts,
        int maximumCount,
        string name)
    {
        var groupSize = counts.Count;
        var total = model.NewIntVar(0, (long)groupSize * maximumCount, $"{name}_total");
        model.Add(total == LinearExpr.Sum(counts));
        var individualDeviations = counts.Select((count, index) =>
        {
            var deviation = model.NewIntVar(0, (long)groupSize * maximumCount, $"{name}_deviation_{index}");
            model.AddAbsEquality(deviation, count * groupSize - total);
            return deviation;
        }).ToArray();
        var rawDeviation = model.NewIntVar(0, (long)groupSize * groupSize * maximumCount, $"{name}_raw");
        model.Add(rawDeviation == LinearExpr.Sum(individualDeviations));
        var normalizedTenths = model.NewIntVar(0, 10L * groupSize * maximumCount, $"{name}_tenths");
        model.AddDivisionEquality(normalizedTenths, rawDeviation * 10, groupSize);
        return normalizedTenths;
    }

    // Returns a fixed historical value before the target month and a decision expression during the modeled month.
    private static LinearExpr WorkShiftIndicator(ScheduleInput input, ModelVariables variables, string employeeId, DateOnly date, Shift shift)
    {
        if (date >= input.DemandMonth.MonthStart) return variables.WorksShift[(employeeId, date, shift)];
        var cell = ResolvedHistoryFor(input, employeeId).FirstOrDefault(item => item.Date == date).Cell;
        return LinearExpr.Constant(cell?.Kind == AssignmentKind.Work && cell.Shift == shift ? 1 : 0);
    }

    // Returns a fixed historical value before the target month and a decision expression during the modeled month.
    private static LinearExpr RestIndicator(ScheduleInput input, ModelVariables variables, string employeeId, DateOnly date)
    {
        if (date >= input.DemandMonth.MonthStart) return variables.AnyRest[(employeeId, date)];
        var cell = ResolvedHistoryFor(input, employeeId).FirstOrDefault(item => item.Date == date).Cell;
        return LinearExpr.Constant(cell?.Kind is AssignmentKind.Rest or AssignmentKind.SpecialRest or AssignmentKind.LeaveRest ? 1 : 0);
    }

    // Converts LB01-LB12 into the four fixed three-station comparison groups.
    private static int StationGroupIndex(string station) => (int.Parse(station[2..]) - 1) / 3;

    private static bool IsWeekendOrNationalHoliday(ScheduleInput input, DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
        || input.RestIntervals.Any(interval => interval.NationalHolidays.Contains(date));

    // State 0 means no prior normal shift; 1-3 identify Early, Afternoon and Night.
    private static int ShiftStateCode(Shift? shift) => shift switch
    {
        Shift.Early => 1,
        Shift.Afternoon => 2,
        Shift.Night => 3,
        _ => 0
    };

    // 已結束工作區段的分段長度懲罰。
    private static int WorkStreakPenaltyValue(int length) => length switch
    {
        0 => 0,
        1 => 4,
        2 => 0,
        3 => 0,
        4 => 0,
        5 => 0,
        _ when length >= 6 => 2 * (length - 4),
        _ => 0
    };

    private sealed record ObjectiveGroup(
        int Priority,
        string Name,
        LinearExpr Total,
        List<(string Name, int Weight, LinearExpr Expression)> Components);
}
