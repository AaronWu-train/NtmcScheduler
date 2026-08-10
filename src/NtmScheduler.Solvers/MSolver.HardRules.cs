using Google.OrTools.Sat;

namespace NtmScheduler.Solvers;

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
            var legalStations = StationsInSameGroup(employee.Affiliation);
            foreach (var date in dates)
            {
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
                if (employee.Assignments.GetValueOrDefault(date)?.RequestedRest != true)
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
            foreach (var station in ExternalStations)
            {
                foreach (var shift in Shifts.Where(shift => RequiredHeadcount(station, shift) > 0))
                {
                    external[(date, station, shift)] = model.NewIntVar(0, RequiredHeadcount(station, shift), $"external_{date:yyyyMMdd}_{station}_{shift}");
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
        RequireMinimumStationCoverage(model, input, dates, variables);
        ForbidOverlappingOrInsufficientlySeparatedWork(model, input, dates, variables);
        RequireGeneralRestInEverySevenDayWindow(model, input, dates, variables);
        EnforceEightWeekRestQuotas(model, input, dates, variables);
    }

    // Hard constraint — each employee/date has exactly one state: normal work, R, R1, R休, or fixed X.
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

    // Hard constraint — do not exceed each employee's target-month R休 limit; null means zero.
    private static void LimitRequestedLeaveRestCount(CpModel model, ScheduleInput input, ModelVariables variables)
    {
        var targetDates = TargetMonthDates(input).ToArray();
        foreach (var employee in input.DemandMonth.Employees)
            model.Add(LinearExpr.Sum(targetDates.Select(date => variables.LeaveRest[(employee.EmployeeId, date)])) <= (employee.RequestedLeaveRestCount ?? 0));
    }

    // Hard constraint — force every supplied normal-work, R, R1, or R休 assignment to its requested value.
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

    // Hard constraint — meet each positive station/shift minimum; zero-demand positions remain forbidden.
    private static void RequireMinimumStationCoverage(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        foreach (var date in dates)
        {
            foreach (var station in Stations)
            {
                foreach (var shift in Shifts)
                {
                    var assigned = variables.Work
                        .Where(x => x.Key.Date == date && x.Key.Station == station && x.Key.Shift == shift)
                        .Select(x => x.Value);
                    LinearExpr coverage = LinearExpr.Sum(assigned);
                    if (variables.External.TryGetValue((date, station, shift), out var external))
                    {
                        coverage += external;
                    }
                    var required = RequiredHeadcount(station, shift);
                    model.Add(required > 0 ? coverage >= required : coverage == 0);
                }
            }
        }
    }

    // Hard constraint — forbid any two actual work intervals that overlap or leave less than eleven hours of rest.
    private static void ForbidOverlappingOrInsufficientlySeparatedWork(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        foreach (var employee in input.DemandMonth.Employees)
        {
            var normal = (from date in dates
                          from shift in Shifts
                          let interval = NormalShiftInterval(date, shift)
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
                    .Select(x => ResolvedWorkInterval(x.Date, x.Cell))
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

    // Hard constraint — every seven consecutive calendar days must contain at least one general rest (R).
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

    // Hard constraint — continue each employee's exact eight-week R and R1 quotas from opening usage or new-hire credit.
    private static void EnforceEightWeekRestQuotas(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        var lastModeledDate = dates[^1];

        foreach (var employee in input.DemandMonth.Employees)
        {
            foreach (var cycle in input.RestIntervals.Where(cycle => dates.Any(date => date >= cycle.Start && date <= cycle.End)))
            {
                var cycleDates = dates.Where(date => date >= cycle.Start && date <= cycle.End && IsEmployedOn(employee, date)).ToArray();
                var prior = RestUsageBeforeModeledDates(input, employee, cycle);
                var rest = LinearExpr.Sum(cycleDates.Select(date => variables.Rest[(employee.EmployeeId, date)]));
                var specialRest = LinearExpr.Sum(cycleDates.Select(date => variables.SpecialRest[(employee.EmployeeId, date)]));
                const int requiredRest = 16;
                var requiredSpecialRest = cycle.NationalHolidays.Count;

                // Close a cycle exactly when it ends in the horizon; otherwise keep its future quota reachable.
                if (cycle.End <= lastModeledDate)
                {
                    model.Add(prior.Rest + rest == requiredRest);
                    model.Add(prior.SpecialRest + specialRest == requiredSpecialRest);
                    continue;
                }

                var futureDays = cycle.End.DayNumber - lastModeledDate.DayNumber;
                model.Add(prior.Rest + rest <= requiredRest);
                model.Add(prior.SpecialRest + specialRest <= requiredSpecialRest);
                model.Add(prior.Rest + rest + futureDays >= requiredRest);
                model.Add(prior.SpecialRest + specialRest + futureDays >= requiredSpecialRest);
                model.Add(prior.Rest + prior.SpecialRest + rest + specialRest + futureDays >= requiredRest + requiredSpecialRest);
            }
        }
    }

    private static int RequiredHeadcount(string station, Shift shift) => shift switch
    {
        Shift.Early or Shift.Afternoon => 1,
        Shift.Night when NightStations.Contains(station) => 1,
        _ => 0
    };

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
