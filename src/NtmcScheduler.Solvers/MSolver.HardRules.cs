using Google.OrTools.Sat;

namespace NtmcScheduler.Solvers;

public static partial class MSolver
{
    // Decision variables

    // Structural hard constraints — create normal-work variables only for the employee's legal station group,
    // and external-headcount variables only for the approved external stations.
    private static ModelVariables CreateDecisionVariables(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates)
    {
        var work = new Dictionary<(string Employee, DateOnly Date, string Station, Shift Shift), BoolVar>();
        var worksShift = new Dictionary<(string Employee, DateOnly Date, Shift Shift), BoolVar>();
        var rest = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var specialRest = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var leaveRest = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var anyRest = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var actualWork = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var supportsOtherStation = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var external = new Dictionary<(DateOnly Date, string Station, Shift Shift), IntVar>();
        var eventDays = input.DemandMonth.Employees
            .SelectMany(employee => employee.Assignments.Where(pair => pair.Value.Kind == AssignmentKind.WorkEvent)
                .Select(pair => (employee.EmployeeId, pair.Key)))
            .ToHashSet();

        foreach (var employee in input.DemandMonth.Employees)
        {
            foreach (var date in dates)
            {
                var fixedStation = employee.Assignments.GetValueOrDefault(date) is { Kind: AssignmentKind.Work } assignment
                    ? assignment.Station
                    : null;
                var legalStations = StationsInSameGroup(input, employee.Affiliation).Append(fixedStation).OfType<string>().Distinct();
                var dayWork = new List<BoolVar>();
                foreach (var shift in Shifts)
                {
                    var shiftWork = model.NewBoolVar($"work_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                    worksShift[(employee.EmployeeId, date, shift)] = shiftWork;
                    var stationWork = new List<BoolVar>();
                    foreach (var station in legalStations)
                    {
                        var variable = model.NewBoolVar($"x_{employee.EmployeeId}_{date:yyyyMMdd}_{station}_{shift}");
                        work[(employee.EmployeeId, date, station, shift)] = variable;
                        stationWork.Add(variable);
                        dayWork.Add(variable);
                    }
                    model.Add(shiftWork == LinearExpr.Sum(stationWork));
                }

                rest[(employee.EmployeeId, date)] = model.NewBoolVar($"rest_{employee.EmployeeId}_{date:yyyyMMdd}");
                specialRest[(employee.EmployeeId, date)] = model.NewBoolVar($"special_rest_{employee.EmployeeId}_{date:yyyyMMdd}");
                leaveRest[(employee.EmployeeId, date)] = model.NewBoolVar($"leave_rest_{employee.EmployeeId}_{date:yyyyMMdd}");
                anyRest[(employee.EmployeeId, date)] = model.NewBoolVar($"any_rest_{employee.EmployeeId}_{date:yyyyMMdd}");
                actualWork[(employee.EmployeeId, date)] = model.NewBoolVar($"actual_work_{employee.EmployeeId}_{date:yyyyMMdd}");
                supportsOtherStation[(employee.EmployeeId, date)] = model.NewBoolVar($"support_{employee.EmployeeId}_{date:yyyyMMdd}");

                // AnyRest means R, R1, or R休. ActualWork means a normal shift or a fixed X event.
                model.Add(anyRest[(employee.EmployeeId, date)] == rest[(employee.EmployeeId, date)] + specialRest[(employee.EmployeeId, date)] + leaveRest[(employee.EmployeeId, date)]);
                if (date.Year != input.DemandMonth.MonthStart.Year || date.Month != input.DemandMonth.MonthStart.Month)
                    model.Add(leaveRest[(employee.EmployeeId, date)] == 0);
                model.Add(actualWork[(employee.EmployeeId, date)] == LinearExpr.Sum(dayWork) + (eventDays.Contains((employee.EmployeeId, date)) ? 1 : 0));
                model.Add(supportsOtherStation[(employee.EmployeeId, date)] == LinearExpr.Sum(
                    work.Where(x => x.Key.Employee == employee.EmployeeId && x.Key.Date == date && x.Key.Station != employee.Affiliation)
                        .Select(x => x.Value)));

                if (!IsEmployedOn(employee, date))
                {
                    model.Add(LinearExpr.Sum(dayWork) == 0);
                    model.Add(rest[(employee.EmployeeId, date)] == 0);
                    model.Add(specialRest[(employee.EmployeeId, date)] == 0);
                    model.Add(leaveRest[(employee.EmployeeId, date)] == 0);
                }
            }
        }

        foreach (var date in dates)
        {
            foreach (var station in input.MonthlySettings!.MStations.Where(x => x.ExternalSupport != ExternalSupportLevel.Disallowed))
            {
                foreach (var shift in Shifts.Where(shift => station.For(shift).Minimum > 0))
                {
                    external[(date, station.Code, shift)] = model.NewIntVar(0, station.For(shift).Minimum, $"external_{date:yyyyMMdd}_{station.Code}_{shift}");
                }
            }
        }

        return new(work, worksShift, rest, specialRest, leaveRest, anyRest, actualWork, supportsOtherStation, external);
    }

    // Hard constraints

    private static void AddHardConstraints(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        AddExactlyOneAssignmentPerActiveDay(model, input, dates, variables);
        LimitRequestedLeaveRestCount(model, input, variables);
        FixSuppliedAssignments(model, input, variables);
        RequireStationCoverage(model, input, dates, variables);
        ForbidOverlappingOrInsufficientlySeparatedWork(model, input, dates, variables);
        RequireGeneralRestInEverySevenDayWindow(model, input, dates, variables);
        EnforceEightWeekRestQuotas(model, input, dates, variables);
    }

    // 每日唯一指派——每位員工每日只能是正常工作、R、R1、R休或固定 X 其中之一。
    private static void AddExactlyOneAssignmentPerActiveDay(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        foreach (var employee in input.DemandMonth.Employees)
        {
            foreach (var date in dates)
            {
                var normalWorkCount = LinearExpr.Sum(variables.Work
                    .Where(item => item.Key.Employee == employee.EmployeeId && item.Key.Date == date)
                    .Select(item => item.Value));
                var fixedWorkEvent = employee.Assignments.GetValueOrDefault(date)?.Kind == AssignmentKind.WorkEvent ? 1 : 0;
                var requiredAssignmentCount = IsEmployedOn(employee, date) ? 1 : 0;

                model.Add(
                    normalWorkCount
                    + variables.Rest[(employee.EmployeeId, date)]
                    + variables.SpecialRest[(employee.EmployeeId, date)]
                    + variables.LeaveRest[(employee.EmployeeId, date)]
                    + fixedWorkEvent
                    == requiredAssignmentCount);
            }
        }
    }

    // 每月 R休 上下界——每位員工的目標月總數必須落在閉區間；null 視為零。
    private static void LimitRequestedLeaveRestCount(CpModel model, ScheduleInput input, ModelVariables variables)
    {
        var targetDates = TargetMonthDates(input).ToArray();
        foreach (var employee in input.DemandMonth.Employees)
        {
            var total = LinearExpr.Sum(targetDates.Select(date => variables.LeaveRest[(employee.EmployeeId, date)]));
            model.Add(total >= (employee.RequestedLeaveRestMinimum ?? 0));
            model.Add(total <= (employee.RequestedLeaveRestCount ?? 0));
        }
    }

    // 固定指派——輸入的正常工作、R、R1 或 R休 必須維持指定值。
    // Fixed X events are already forced because AddExactlyOneAssignmentPerActiveDay inserts their constant value of one.
    private static void FixSuppliedAssignments(CpModel model, ScheduleInput input, ModelVariables variables)
    {
        foreach (var employee in input.DemandMonth.Employees)
        {
            foreach (var assignment in employee.Assignments.Where(pair => pair.Value.Kind is not null))
            {
                switch (assignment.Value.Kind)
                {
                    case AssignmentKind.Work:
                        model.Add(variables.Work[(employee.EmployeeId, assignment.Key, assignment.Value.Station!, assignment.Value.Shift!.Value)] == 1);
                        break;
                    case AssignmentKind.Rest:
                        model.Add(variables.Rest[(employee.EmployeeId, assignment.Key)] == 1);
                        break;
                    case AssignmentKind.SpecialRest:
                        model.Add(variables.SpecialRest[(employee.EmployeeId, assignment.Key)] == 1);
                        break;
                    case AssignmentKind.LeaveRest:
                        model.Add(variables.LeaveRest[(employee.EmployeeId, assignment.Key)] == 1);
                        break;
                }
            }
        }
    }

    // 班位覆蓋——每站班依月份設定限制總人數；外援只補內部人力未達最低需求的差額。
    private static void RequireStationCoverage(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        foreach (var date in dates)
        {
            foreach (var station in Stations(input))
            {
                foreach (var shift in Shifts)
                {
                    var assigned = variables.Work
                        .Where(x => x.Key.Date == date && x.Key.Station == station && x.Key.Shift == shift)
                        .Select(x => x.Value).ToArray();
                    var range = input.MonthlySettings!.MStations.Single(x => x.Code == station).For(shift);
                    LinearExpr coverage = LinearExpr.Sum(assigned);
                    if (variables.External.TryGetValue((date, station, shift), out var external))
                    {
                        model.AddMaxEquality(external, [range.Minimum - LinearExpr.Sum(assigned), LinearExpr.Constant(0)]);
                        coverage += external;
                    }
                    model.Add(coverage >= range.Minimum);
                    model.Add(coverage <= range.Maximum);
                }
            }
        }
    }

    // 最少十一小時休息——禁止實際工作區間重疊或間隔少於十一小時。
    private static void ForbidOverlappingOrInsufficientlySeparatedWork(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        var times = input.StandardShiftTimes?.M;
        foreach (var employee in input.DemandMonth.Employees)
        {
            var normal = (from date in dates
                          from shift in Shifts
                          let interval = NormalShiftInterval(date, shift, times)
                          select (date, shift, interval.Start, interval.End, Variable: variables.WorksShift[(employee.EmployeeId, date, shift)]))
                .ToArray();

            for (var i = 0; i < normal.Length; i++)
            {
                for (var j = i + 1; j < normal.Length; j++)
                {
                    if (OverlapsOrLeavesLessThanMinimumRest(normal[i].Start, normal[i].End, normal[j].Start, normal[j].End))
                    {
                        model.Add(normal[i].Variable + normal[j].Variable <= 1);
                    }
                }
            }

            var fixedIntervals = employee.Assignments
                .Where(pair => pair.Value.Kind == AssignmentKind.WorkEvent)
                .Select(pair => (pair.Value.EventStart!.Value, pair.Value.EventEnd!.Value))
                .Concat(ResolvedHistoryFor(input, employee.EmployeeId)
                    .Select(x => ResolvedWorkInterval(x.Date, x.Cell, times))
                    .Where(x => x is not null)
                    .Select(x => (x!.Value.Start, x.Value.End)))
                .ToArray();

            foreach (var assignment in normal)
            {
                if (fixedIntervals.Any(interval => OverlapsOrLeavesLessThanMinimumRest(interval.Item1, interval.Item2, assignment.Start, assignment.End)))
                {
                    model.Add(assignment.Variable == 0);
                }
            }
        }
    }

    // 連續七日至少一日一般 R——每個連續七日視窗都必須包含至少一日 R。
    private static void RequireGeneralRestInEverySevenDayWindow(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        // Only R resets this seven-day window; R1 is intentionally not a general-rest reset.
        var modeledDates = dates.ToHashSet();
        foreach (var employee in input.DemandMonth.Employees)
        {
            var historicalGeneralRest = ResolvedHistoryFor(input, employee.EmployeeId)
                .Where(x => x.Cell.Kind == AssignmentKind.Rest)
                .Select(x => x.Date)
                .ToHashSet();

            foreach (var end in dates.Where(date => IsEmployedOn(employee, date.AddDays(-6))))
            {
                var window = Enumerable.Range(0, 7).Select(offset => end.AddDays(-offset)).ToArray();
                var fixedRest = window.Count(historicalGeneralRest.Contains);
                var decidedRest = window.Where(modeledDates.Contains).Select(date => variables.Rest[(employee.EmployeeId, date)]);
                model.Add(LinearExpr.Sum(decidedRest) + fixedRest >= 1);
            }
        }
    }

    // 八週休假額度——從期初用量或新進折抵承接每人的精確 R 與 R1 額度。
    private static void EnforceEightWeekRestQuotas(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        var lastModeledDate = dates[^1];

        foreach (var employee in input.DemandMonth.Employees)
        {
            foreach (var cycle in input.RestIntervals.Where(cycle => dates.Any(date => date >= cycle.Start && date <= cycle.End)))
            {
                var cycleDates = dates.Where(date => date >= cycle.Start && date <= cycle.End && IsEmployedOn(employee, date)).ToArray();
                var prior = RestUsageBeforeModeledDates(input, employee, cycle);
                var after = RestUsageAfterModeledDates(employee, cycle);
                var rest = LinearExpr.Sum(cycleDates.Select(date => variables.Rest[(employee.EmployeeId, date)]));
                var specialRest = LinearExpr.Sum(cycleDates.Select(date => variables.SpecialRest[(employee.EmployeeId, date)]));
                const int requiredRest = 16;
                var requiredSpecialRest = cycle.NationalHolidays.Count;

                // Close a cycle exactly when it ends in the horizon; otherwise keep its future quota reachable.
                if (cycle.End <= lastModeledDate)
                {
                    model.Add(prior.Rest + after.Rest + rest == requiredRest);
                    model.Add(prior.SpecialRest + after.SpecialRest + specialRest == requiredSpecialRest);
                    continue;
                }

                var futureEnd = employee.EmploymentEndDate is { } end && end < cycle.End ? end : cycle.End;
                var futureDays = Math.Max(0, futureEnd.DayNumber - lastModeledDate.DayNumber);
                model.Add(prior.Rest + after.Rest + rest <= requiredRest);
                model.Add(prior.SpecialRest + after.SpecialRest + specialRest <= requiredSpecialRest);
                model.Add(prior.Rest + after.Rest + rest + futureDays >= requiredRest);
                model.Add(prior.SpecialRest + after.SpecialRest + specialRest + futureDays >= requiredSpecialRest);
                model.Add(prior.Rest + prior.SpecialRest + after.Rest + after.SpecialRest + rest + specialRest + futureDays >= requiredRest + requiredSpecialRest);
            }
        }
    }

    private static int RequiredHeadcount(ScheduleInput input, string station, Shift shift) =>
        input.MonthlySettings!.MStations.Single(x => x.Code == station).For(shift).Minimum;

    private sealed record ModelVariables(
        Dictionary<(string Employee, DateOnly Date, string Station, Shift Shift), BoolVar> Work,
        Dictionary<(string Employee, DateOnly Date, Shift Shift), BoolVar> WorksShift,
        Dictionary<(string Employee, DateOnly Date), BoolVar> Rest,
        Dictionary<(string Employee, DateOnly Date), BoolVar> SpecialRest,
        Dictionary<(string Employee, DateOnly Date), BoolVar> LeaveRest,
        Dictionary<(string Employee, DateOnly Date), BoolVar> AnyRest,
        Dictionary<(string Employee, DateOnly Date), BoolVar> ActualWork,
        Dictionary<(string Employee, DateOnly Date), BoolVar> SupportsOtherStation,
        Dictionary<(DateOnly Date, string Station, Shift Shift), IntVar> External);
}
