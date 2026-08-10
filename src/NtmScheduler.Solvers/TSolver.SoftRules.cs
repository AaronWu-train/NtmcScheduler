using Google.OrTools.Sat;

namespace NtmScheduler.Solvers;

public static partial class TSolver
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
        var nonMonthlyShift = CountNonMonthlyShiftAssignments(input, modelDates, variables);
        var attendance = MeasureAttendanceShortfall(model, input, targetDates, variables);
        var specialty = CountMissingSpecialties(model, input, targetDates, variables);
        var ability = MeasureAbilityShortfall(model, input, targetDates, variables);
        var monthlyRest = MeasureMonthlyRestDeviation(model, input, targetDates, variables.Rest, false, "monthly_rest");
        var monthlySpecialRest = MeasureMonthlyRestDeviation(model, input, targetDates, variables.SpecialRest, true, "monthly_special_rest");
        var workStreak = MeasureWorkStreakPenalties(model, input, targetDates, modelDates, variables);
        var transitionRest = MeasureNightToEarlyRestShortfall(model, input, targetDates, variables);
        var boundaryBalance = MeasureMonthBoundaryRestDifference(model, input, targetDates, variables);
        var weekdayFairness = MeasureRestCountRangeByMonthlyShift(model, input, targetDates.Where(date => !IsWeekendOrNationalHoliday(input, date)), variables, "weekday_fairness");
        var holidayFairness = MeasureRestCountRangeByMonthlyShift(model, input, targetDates.Where(date => IsWeekendOrNationalHoliday(input, date)), variables, "holiday_fairness");

        return
        [
            new(1, "RequestedRest", requestedRest, [("RequestedRest", 1, requestedRest)]),
            new(2, "StaffingQuality",
                nonMonthlyShift * 9 + attendance * 9 + specialty * 3 + ability,
                [("NonMonthlyShift", 9, nonMonthlyShift), ("Attendance", 9, attendance), ("Specialty", 3, specialty), ("Ability", 1, ability)]),
            new(3, "MonthlyRestDistribution",
                monthlyRest + monthlySpecialRest,
                [("MonthlyRest", 1, monthlyRest), ("MonthlySpecialRest", 1, monthlySpecialRest)]),
            new(4, "WorkPatternQuality",
                workStreak * 3 + transitionRest * 12 + boundaryBalance * 5,
                [("WorkStreak", 3, workStreak), ("NightToEarlyRest", 12, transitionRest), ("MonthBoundaryRestBalance", 5, boundaryBalance)]),
            new(5, "RestFairness",
                weekdayFairness * 2 + holidayFairness * 4,
                [("WeekdayRestFairness", 2, weekdayFairness), ("HolidayRestFairness", 4, holidayFairness)])
        ];
    }

    // Requested rest — count R* cells whose result is not an actual rest.
    private static LinearExpr CountUnfulfilledRequestedRests(ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables) =>
        LinearExpr.Sum(from employee in input.DemandMonth.Employees
            from date in targetDates
            where employee.Assignments.GetValueOrDefault(date)?.RequestedRest == true
            select 1 - variables.AnyRest[(employee.EmployeeId, date)]);

    // Monthly-shift adherence — count normal-work cells outside the target or rotated extension shift.
    private static LinearExpr CountNonMonthlyShiftAssignments(ScheduleInput input, IReadOnlyList<DateOnly> modelDates, ModelVariables variables) =>
        LinearExpr.Sum(from employee in input.DemandMonth.Employees
            from date in modelDates
            from shift in Shifts
            where shift != MonthlyShiftOnDate(employee, date, input.DemandMonth.MonthStart)
            select variables.Work[(employee.EmployeeId, date, shift)]);

    // Attendance — compare the monthly group's target with everyone actually working that shift.
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

    // Specialty — a transfer can cover a specialty required by the receiving monthly group.
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

    // Ability — score everyone actually working the shift, including transfers.
    private static LinearExpr MeasureAbilityShortfall(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> targetDates, ModelVariables variables)
    {
        var deficits = new List<IntVar>();
        foreach (var date in targetDates)
        foreach (var shift in Shifts)
        {
            var workers = input.DemandMonth.Employees.Where(employee => IsEmployedOn(employee, date)).ToArray();
            var attendance = LinearExpr.Sum(workers.Select(employee => variables.Work[(employee.EmployeeId, date, shift)]));
            var ability = LinearExpr.Sum(workers.Select(employee => variables.Work[(employee.EmployeeId, date, shift)] * employee.Ability!.Value));
            var deficit = model.NewIntVar(0, workers.Length * 2L, $"ability_deficit_{date:yyyyMMdd}_{shift}");
            model.AddMaxEquality(deficit, [3 * attendance - ability, LinearExpr.Constant(0)]);
            deficits.Add(deficit);
        }
        return LinearExpr.Sum(deficits);
    }

    // Monthly rest distribution — derive each employee's R/R1 target from active weekends/holidays.
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
            var excess = model.NewIntVar(0, maximum, $"{name}_excess_{employee.EmployeeId}");
            var penalty = model.NewIntVar(0, (long)maximum * maximum, $"{name}_penalty_{employee.EmployeeId}");
            model.Add(count == LinearExpr.Sum(activeDates.Select(date => rests[(employee.EmployeeId, date)])));
            model.AddAbsEquality(deviation, count - target);
            model.AddMaxEquality(excess, [deviation - 1, LinearExpr.Constant(0)]);
            model.AddMultiplicationEquality(penalty, excess, excess);
            penalties.Add(penalty);
        }
        return LinearExpr.Sum(penalties);
    }

    // Work streak — score completed actual-work streaks; R, R1, and R休 end a streak.
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

    // Night-to-early transition — derive rest after the last actual historical night; no night means no penalty.
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

    // Month-boundary balance — compare rest on the two dates around a real night-to-early transition.
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

    // Rest fairness — compare only full-month employees inside the same T monthly shift.
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

    private static bool IsWeekendOrNationalHoliday(ScheduleInput input, DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
        || input.RestIntervals.Any(interval => interval.NationalHolidays.Contains(date));

    // Piecewise penalty for completed work-streak lengths.
    private static int BlockLengthPenaltyValue(int length) => length switch
    {
        0 => 0,
        1 => 4,
        2 => 2,
        3 => 1,
        4 or 5 => 0,
        _ => 2 * (length - 5)
    };

    private sealed record ObjectiveGroup(
        int Priority,
        string Name,
        LinearExpr Total,
        List<(string Name, int Weight, LinearExpr Expression)> Components);
}
