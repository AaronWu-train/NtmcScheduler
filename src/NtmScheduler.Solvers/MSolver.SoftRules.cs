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
        var requestedRest = CountUnfulfilledRequestedRests(input, targetDates, variables);
        var unusedLeaveRest = MeasureUnusedLeaveRests(model, input, targetDates, variables);
        var externalStaffing = CountExternalStaffing(targetDates, variables);
        var monthlyRest = MeasureMonthlyRestDeviation(model, input, targetDates, variables.Rest, false, "monthly_rest");
        var monthlySpecialRest = MeasureMonthlyRestDeviation(model, input, targetDates, variables.SpecialRest, true, "monthly_special_rest");
        var nonHomeStation = CountCrossStationAssignments(input, targetDates, variables);
        var workStreak = MeasureWorkStreakPenalties(model, input, targetDates, modelDates, variables);
        var sameShiftBlock = MeasureSameShiftBlockPenalties(model, input, targetDates, variables);
        var nightRestEarly = CountNightRestShiftPatterns(model, input, variables, Shift.Early, "night_rest_early");
        var nightRestAfternoon = CountNightRestShiftPatterns(model, input, variables, Shift.Afternoon, "night_rest_afternoon");
        var shiftChangeWithoutRest = CountShiftChangesWithoutRest(model, input, modelDates, variables);
        var rotation = CountNonPreferredRotations(model, input, modelDates, variables);
        var weekdayFairness = MeasureRestCountRangeByStationGroup(model, input, targetDates.Where(date => !IsWeekendOrNationalHoliday(input, date)), variables, "weekday_fairness");
        var holidayFairness = MeasureRestCountRangeByStationGroup(model, input, targetDates.Where(date => IsWeekendOrNationalHoliday(input, date)), variables, "holiday_fairness");
        var supportFairness = MeasureSupportCountRangeByStationGroup(model, input, targetDates, variables);
        var earlyShiftFairness = MeasureShiftCountDispersionByStationGroup(model, input, targetDates, variables, Shift.Early);
        var afternoonShiftFairness = MeasureShiftCountDispersionByStationGroup(model, input, targetDates, variables, Shift.Afternoon);
        var nightShiftFairness = MeasureShiftCountDispersionByStationGroup(model, input, targetDates, variables, Shift.Night);

        return
        [
            new(1, "RequestedRest", requestedRest * 3 + unusedLeaveRest,
                [("RequestedRest", 3, requestedRest), ("UnusedLeaveRest", 1, unusedLeaveRest)]),
            new(2, "ExternalStaffing", externalStaffing, [("ExternalStaffing", 1, externalStaffing)]),
            new(3, "MonthlyRestDistribution",
                monthlyRest * 4 + monthlySpecialRest * 8,
                [("MonthlyRest", 4, monthlyRest), ("MonthlySpecialRest", 8, monthlySpecialRest)]),
            new(4, "ScheduleQuality",
                nonHomeStation * 8 + workStreak * 3 + sameShiftBlock * 2 + nightRestEarly * 12
                    + nightRestAfternoon * 8 + shiftChangeWithoutRest * 6,
                [
                    ("NonHomeStation", 8, nonHomeStation),
                    ("WorkStreak", 3, workStreak),
                    ("SameShiftBlock", 2, sameShiftBlock),
                    ("NightRestEarly", 12, nightRestEarly),
                    ("NightRestAfternoon", 8, nightRestAfternoon),
                    ("ShiftChangeWithoutRest", 6, shiftChangeWithoutRest)
                ]),
            new(5, "RotationAndFairness",
                rotation + weekdayFairness * 2 + holidayFairness * 4 + supportFairness * 3
                    + earlyShiftFairness + afternoonShiftFairness + nightShiftFairness * 2,
                [
                    ("NonPreferredRotation", 1, rotation),
                    ("WeekdayRestFairness", 2, weekdayFairness),
                    ("HolidayRestFairness", 4, holidayFairness),
                    ("SupportFairness", 3, supportFairness),
                    ("EarlyShiftFairness", 1, earlyShiftFairness),
                    ("AfternoonShiftFairness", 1, afternoonShiftFairness),
                    ("NightShiftFairness", 2, nightShiftFairness)
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

    // 外援人力——最小化目標月的外派總人次。
    private static LinearExpr CountExternalStaffing(IReadOnlyList<DateOnly> targetDates, ModelVariables variables) =>
        LinearExpr.Sum(variables.External.Where(x => targetDates.Contains(x.Key.Date)).Select(x => x.Value));

    // 每月休假分布——依到職後的週末與國定假日推導每人的 R/R1 目標。
    private static LinearExpr MeasureMonthlyRestDeviation(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        IReadOnlyDictionary<(string Employee, DateOnly Date), BoolVar> rests,
        bool special,
        string name)
    {
        var penalties = new List<IntVar>();
        foreach (var employee in input.DemandMonth.Employees)
        {
            var activeDates = targetDates.Where(date => IsEmployedOn(employee, date)).ToArray();
            var target = ExpectedMonthlyRestCount(input, employee, special);
            var maximum = Math.Max(activeDates.Length, target);
            var count = model.NewIntVar(0, activeDates.Length, $"{name}_count_{employee.EmployeeId}");
            var deviation = model.NewIntVar(0, maximum, $"{name}_deviation_{employee.EmployeeId}");
            var penalty = model.NewIntVar(0, (long)maximum * maximum, $"{name}_penalty_{employee.EmployeeId}");
            model.Add(count == LinearExpr.Sum(activeDates.Select(date => rests[(employee.EmployeeId, date)])));
            model.AddAbsEquality(deviation, count - target);
            model.AddMultiplicationEquality(penalty, deviation, deviation);
            penalties.Add(penalty);
        }
        return LinearExpr.Sum(penalties);
    }

    // 非所屬站指派——計算不在員工所屬站工作的日數。
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
                    model.AddElement(count, Enumerable.Range(0, maximumLength + 1).Select(BlockLengthPenaltyValue), rawPenalty);
                    model.AddMultiplicationEquality(penalty, rawPenalty, streakEnds);
                    penalties.Add(penalty);
                }
                previousCount = count;
            }
        }
        return LinearExpr.Sum(penalties);
    }

    // 同班別區塊——只看目標月並忽略車站，班別序列略過 R/R1/R休/X。
    private static LinearExpr MeasureSameShiftBlockPenalties(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        ModelVariables variables)
    {
        var penalties = new List<LinearExpr>();
        foreach (var employee in input.DemandMonth.Employees)
        {
            LinearExpr previousShift = LinearExpr.Constant(0);
            LinearExpr previousLength = LinearExpr.Constant(0);
            foreach (var date in targetDates.Where(date => IsEmployedOn(employee, date)))
            {
                var hasNormal = model.NewBoolVar($"same_shift_has_work_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.Add(hasNormal == LinearExpr.Sum(Shifts.Select(shift => variables.WorksShift[(employee.EmployeeId, date, shift)])));
                var lastShift = model.NewIntVar(0, 3, $"same_shift_last_{employee.EmployeeId}_{date:yyyyMMdd}");
                var blockLength = model.NewIntVar(0, targetDates.Count, $"same_shift_length_{employee.EmployeeId}_{date:yyyyMMdd}");
                model.Add(lastShift == previousShift).OnlyEnforceIf(hasNormal.Not());
                model.Add(blockLength == previousLength).OnlyEnforceIf(hasNormal.Not());

                foreach (var shift in Shifts)
                {
                    var normal = variables.WorksShift[(employee.EmployeeId, date, shift)];
                    var same = model.NewBoolVar($"same_shift_previous_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                    model.Add(previousShift == ShiftStateCode(shift)).OnlyEnforceIf(same);
                    model.Add(previousShift != ShiftStateCode(shift)).OnlyEnforceIf(same.Not());

                    var continues = model.NewBoolVar($"same_shift_continues_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                    model.Add(continues <= normal);
                    model.Add(continues <= same);
                    model.Add(continues >= normal + same - 1);

                    var starts = model.NewBoolVar($"same_shift_starts_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                    model.Add(starts + continues == normal);
                    model.Add(lastShift == ShiftStateCode(shift)).OnlyEnforceIf(normal);
                    model.Add(blockLength == previousLength + 1).OnlyEnforceIf(continues);
                    model.Add(blockLength == 1).OnlyEnforceIf(starts);

                    var rawPenalty = model.NewIntVar(0, 2L * targetDates.Count, $"same_shift_penalty_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}_raw");
                    var penalty = model.NewIntVar(0, 2L * targetDates.Count, $"same_shift_penalty_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                    model.AddElement(previousLength, Enumerable.Range(0, targetDates.Count + 1).Select(BlockLengthPenaltyValue), rawPenalty);
                    model.AddMultiplicationEquality(penalty, rawPenalty, starts);
                    penalties.Add(penalty);
                }

                previousShift = lastShift;
                previousLength = blockLength;
            }

            var finalPenalty = model.NewIntVar(0, 2L * targetDates.Count, $"same_shift_final_{employee.EmployeeId}");
            model.AddElement(previousLength, Enumerable.Range(0, targetDates.Count + 1).Select(BlockLengthPenaltyValue), finalPenalty);
            penalties.Add(finalPenalty);
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

    // 偏好班別輪轉——早 -> 午 -> 夜 -> 早；序列略過 R/R1/R休/X。
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
    private static LinearExpr MeasureRestCountRangeByStationGroup(
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
                     .GroupBy(employee => StationGroupIndex(employee.Affiliation)))
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

    // 跨站支援公平——只比較同站群組且全月在職人員的跨站支援數。
    private static LinearExpr MeasureSupportCountRangeByStationGroup(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        ModelVariables variables)
    {
        var ranges = new List<LinearExpr>();
        foreach (var group in input.DemandMonth.Employees
                     .Where(employee => IsEmployedOn(employee, input.DemandMonth.MonthStart))
                     .GroupBy(employee => StationGroupIndex(employee.Affiliation)))
        {
            var counts = group.Select(employee =>
            {
                var count = model.NewIntVar(0, targetDates.Count, $"support_fairness_{employee.EmployeeId}");
                model.Add(count == LinearExpr.Sum(targetDates.Select(date => variables.SupportsOtherStation[(employee.EmployeeId, date)])));
                return (LinearExpr)count;
            }).ToArray();
            if (counts.Length < 2) continue;
            var maximum = model.NewIntVar(0, targetDates.Count, $"support_fairness_max_{group.Key}");
            var minimum = model.NewIntVar(0, targetDates.Count, $"support_fairness_min_{group.Key}");
            model.AddMaxEquality(maximum, counts);
            model.AddMinEquality(minimum, counts);
            ranges.Add(maximum - minimum);
        }
        return LinearExpr.Sum(ranges);
    }

    // 班別次數公平——加總各全月在職站群的整數變異數分子 n*sum(x²)-sum(x)²。
    private static LinearExpr MeasureShiftCountDispersionByStationGroup(
        CpModel model,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        ModelVariables variables,
        Shift shift)
    {
        var dispersions = new List<LinearExpr>();
        foreach (var group in input.DemandMonth.Employees
                     .Where(employee => IsEmployedOn(employee, input.DemandMonth.MonthStart))
                     .GroupBy(employee => StationGroupIndex(employee.Affiliation)))
        {
            var members = group.ToArray();
            if (members.Length < 2) continue;
            var counts = members.Select(employee =>
            {
                var count = model.NewIntVar(0, targetDates.Count, $"shift_fairness_count_{group.Key}_{shift}_{employee.EmployeeId}");
                model.Add(count == LinearExpr.Sum(targetDates.Select(date => variables.WorksShift[(employee.EmployeeId, date, shift)])));
                return count;
            }).ToArray();
            var squares = counts.Select((count, index) =>
            {
                var square = model.NewIntVar(0, (long)targetDates.Count * targetDates.Count, $"shift_fairness_square_{group.Key}_{shift}_{index}");
                model.AddMultiplicationEquality(square, count, count);
                return square;
            }).ToArray();
            var total = model.NewIntVar(0, members.Length * targetDates.Count, $"shift_fairness_total_{group.Key}_{shift}");
            var totalSquare = model.NewIntVar(0, (long)members.Length * members.Length * targetDates.Count * targetDates.Count, $"shift_fairness_total_square_{group.Key}_{shift}");
            model.Add(total == LinearExpr.Sum(counts));
            model.AddMultiplicationEquality(totalSquare, total, total);
            dispersions.Add(LinearExpr.Sum(squares) * members.Length - totalSquare);
        }
        return LinearExpr.Sum(dispersions);
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

    // Shared piecewise penalty for completed work-streak and same-shift-block lengths.
    private static int BlockLengthPenaltyValue(int length) => length switch
    {
        0 => 0,
        1 => 4,
        2 => 3,
        3 => 1,
        4 => 0,
        5 => 2,
        _ when length >= 6 => 5,
        _ => 0
    };

    private sealed record ObjectiveGroup(
        int Priority,
        string Name,
        LinearExpr Total,
        List<(string Name, int Weight, LinearExpr Expression)> Components);
}
