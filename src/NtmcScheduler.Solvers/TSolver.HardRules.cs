using Google.OrTools.Sat;

namespace NtmcScheduler.Solvers;

public static partial class TSolver
{
    // Decision variables

    private static ModelVariables CreateDecisionVariables(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates)
    {
        var work = new Dictionary<(string Employee, DateOnly Date, Shift Shift), BoolVar>();
        var rest = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var specialRest = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var leaveRest = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var anyRest = new Dictionary<(string Employee, DateOnly Date), BoolVar>();
        var actualWork = new Dictionary<(string Employee, DateOnly Date), BoolVar>();

        foreach (var employee in input.DemandMonth.Employees)
        {
            foreach (var date in dates)
            {
                foreach (var shift in Shifts)
                    work[(employee.EmployeeId, date, shift)] = model.NewBoolVar($"work_{employee.EmployeeId}_{date:yyyyMMdd}_{shift}");
                rest[(employee.EmployeeId, date)] = model.NewBoolVar($"rest_{employee.EmployeeId}_{date:yyyyMMdd}");
                specialRest[(employee.EmployeeId, date)] = model.NewBoolVar($"special_rest_{employee.EmployeeId}_{date:yyyyMMdd}");
                leaveRest[(employee.EmployeeId, date)] = model.NewBoolVar($"leave_rest_{employee.EmployeeId}_{date:yyyyMMdd}");
                anyRest[(employee.EmployeeId, date)] = model.NewBoolVar($"any_rest_{employee.EmployeeId}_{date:yyyyMMdd}");
                actualWork[(employee.EmployeeId, date)] = model.NewBoolVar($"actual_work_{employee.EmployeeId}_{date:yyyyMMdd}");

                var eventValue = employee.Assignments.GetValueOrDefault(date)?.Kind == AssignmentKind.WorkEvent ? 1 : 0;
                // X is actual work for rest spacing, but it is not a normal monthly-shift assignment.
                model.Add(anyRest[(employee.EmployeeId, date)] == rest[(employee.EmployeeId, date)] + specialRest[(employee.EmployeeId, date)] + leaveRest[(employee.EmployeeId, date)]);
                if (employee.Assignments.GetValueOrDefault(date)?.RequestedRest != true)
                    model.Add(leaveRest[(employee.EmployeeId, date)] == 0);
                model.Add(actualWork[(employee.EmployeeId, date)] == LinearExpr.Sum(Shifts.Select(shift => work[(employee.EmployeeId, date, shift)])) + eventValue);
                if (!IsEmployedOn(employee, date))
                {
                    model.Add(LinearExpr.Sum(Shifts.Select(shift => work[(employee.EmployeeId, date, shift)])) == 0);
                    model.Add(rest[(employee.EmployeeId, date)] == 0);
                    model.Add(specialRest[(employee.EmployeeId, date)] == 0);
                    model.Add(leaveRest[(employee.EmployeeId, date)] == 0);
                }
            }
        }
        return new(work, rest, specialRest, leaveRest, anyRest, actualWork);
    }

    // Hard constraints

    private static void AddHardConstraints(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        AddExactlyOneAssignmentPerActiveDay(model, input, dates, variables);
        LimitRequestedLeaveRestCount(model, input, variables);
        FixSuppliedAssignments(model, input, variables);
        ForbidOverlappingOrInsufficientlySeparatedWork(model, input, dates, variables);
        RequireGeneralRestInEverySevenDayWindow(model, input, dates, variables);
        EnforceEightWeekRestQuotas(model, input, dates, variables);
    }

    // 每日唯一指派——每位在職員工每日只能是正常工作、R、R1、R休或固定 X 其中之一。
    private static void AddExactlyOneAssignmentPerActiveDay(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        foreach (var employee in input.DemandMonth.Employees)
            foreach (var date in dates)
            {
                var normalWorkCount = LinearExpr.Sum(Shifts.Select(shift => variables.Work[(employee.EmployeeId, date, shift)]));
                var fixedWorkEvent = employee.Assignments.GetValueOrDefault(date)?.Kind == AssignmentKind.WorkEvent ? 1 : 0;
                var requiredAssignmentCount = IsEmployedOn(employee, date) ? 1 : 0;

                model.Add(normalWorkCount
                    + variables.Rest[(employee.EmployeeId, date)]
                    + variables.SpecialRest[(employee.EmployeeId, date)]
                    + variables.LeaveRest[(employee.EmployeeId, date)]
                    + fixedWorkEvent
                    == requiredAssignmentCount);
            }
    }

    // 每月 R休 上限——不得超過每位員工的目標月上限；null 視為零。
    private static void LimitRequestedLeaveRestCount(CpModel model, ScheduleInput input, ModelVariables variables)
    {
        var targetDates = TargetMonthDates(input).ToArray();
        foreach (var employee in input.DemandMonth.Employees)
            model.Add(LinearExpr.Sum(targetDates.Select(date => variables.LeaveRest[(employee.EmployeeId, date)])) <= (employee.RequestedLeaveRestCount ?? 0));
    }

    // 固定指派——輸入的正常工作、R、R1 或 R休 必須維持指定值；X 由每日等式固定。
    private static void FixSuppliedAssignments(CpModel model, ScheduleInput input, ModelVariables variables)
    {
        foreach (var employee in input.DemandMonth.Employees)
            foreach (var assignment in employee.Assignments.Where(pair => pair.Value.Kind is not null))
            {
                switch (assignment.Value.Kind)
                {
                    case AssignmentKind.Work:
                        model.Add(variables.Work[(employee.EmployeeId, assignment.Key, assignment.Value.Shift!.Value)] == 1);
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

    // 最少十一小時休息——禁止工作區間重疊或間隔少於十一小時。
    private static void ForbidOverlappingOrInsufficientlySeparatedWork(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        foreach (var employee in input.DemandMonth.Employees)
        {
            var normal = (from date in dates
                          from shift in Shifts
                          let interval = NormalShiftInterval(date, shift)
                          select (interval.Start, interval.End, Variable: variables.Work[(employee.EmployeeId, date, shift)]))
                .ToArray();
            for (var first = 0; first < normal.Length; first++)
                for (var second = first + 1; second < normal.Length; second++)
                    if (OverlapsOrLeavesLessThanMinimumRest(normal[first].Start, normal[first].End, normal[second].Start, normal[second].End))
                        model.Add(normal[first].Variable + normal[second].Variable <= 1);

            var fixedIntervals = employee.Assignments
                .Where(pair => pair.Value.Kind == AssignmentKind.WorkEvent)
                .Select(pair => (pair.Value.EventStart!.Value, pair.Value.EventEnd!.Value))
                .Concat(ResolvedHistoryFor(input, employee.EmployeeId)
                    .Select(item => ResolvedWorkInterval(item.Date, item.Cell))
                    .Where(item => item is not null)
                    .Select(item => (item!.Value.Start, item.Value.End)))
                .ToArray();
            foreach (var assignment in normal)
                if (fixedIntervals.Any(interval => OverlapsOrLeavesLessThanMinimumRest(interval.Item1, interval.Item2, assignment.Start, assignment.End)))
                    model.Add(assignment.Variable == 0);
        }
    }

    // 連續七日至少一日一般 R——到職滿七日後，每個七日視窗都必須包含 R。
    private static void RequireGeneralRestInEverySevenDayWindow(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        var modeledDates = dates.ToHashSet();
        foreach (var employee in input.DemandMonth.Employees)
        {
            var historicalRest = ResolvedHistoryFor(input, employee.EmployeeId)
                .Where(item => item.Cell.Kind == AssignmentKind.Rest)
                .Select(item => item.Date)
                .ToHashSet();
            foreach (var end in dates.Where(date => IsEmployedOn(employee, date.AddDays(-6))))
            {
                var window = Enumerable.Range(0, 7).Select(offset => end.AddDays(-offset)).ToArray();
                model.Add(LinearExpr.Sum(window.Where(modeledDates.Contains).Select(date => variables.Rest[(employee.EmployeeId, date)])) + window.Count(historicalRest.Contains) >= 1);
            }
        }
    }

    // 八週休假額度——承接精確 56 日額度：16 日 R，且每個國定假日對應一日 R1。
    private static void EnforceEightWeekRestQuotas(CpModel model, ScheduleInput input, IReadOnlyList<DateOnly> dates, ModelVariables variables)
    {
        var lastModeledDate = dates[^1];
        foreach (var employee in input.DemandMonth.Employees)
            foreach (var interval in input.RestIntervals.Where(interval => dates.Any(date => date >= interval.Start && date <= interval.End)))
            {
                var intervalDates = dates.Where(date => date >= interval.Start && date <= interval.End && IsEmployedOn(employee, date)).ToArray();
                var prior = RestUsageBeforeModeledDates(input, employee, interval);
                var rest = LinearExpr.Sum(intervalDates.Select(date => variables.Rest[(employee.EmployeeId, date)]));
                var specialRest = LinearExpr.Sum(intervalDates.Select(date => variables.SpecialRest[(employee.EmployeeId, date)]));
                const int requiredRest = 16;
                var requiredSpecialRest = interval.NationalHolidays.Count;

                if (interval.End <= lastModeledDate)
                {
                    model.Add(prior.Rest + rest == requiredRest);
                    model.Add(prior.SpecialRest + specialRest == requiredSpecialRest);
                    continue;
                }

                var futureDays = interval.End.DayNumber - lastModeledDate.DayNumber;
                model.Add(prior.Rest + rest <= requiredRest);
                model.Add(prior.SpecialRest + specialRest <= requiredSpecialRest);
                model.Add(prior.Rest + rest + futureDays >= requiredRest);
                model.Add(prior.SpecialRest + specialRest + futureDays >= requiredSpecialRest);
                model.Add(prior.Rest + prior.SpecialRest + rest + specialRest + futureDays >= requiredRest + requiredSpecialRest);
            }
    }

    // Target-month preference uses T月班別; seven extension dates use the next monthly rotation.
    private static Shift MonthlyShiftOnDate(EmployeeMonthlySchedule employee, DateOnly date, DateOnly monthStart) =>
        date < monthStart.AddMonths(1) ? employee.MonthlyShift!.Value : NextMonthlyShift(employee.MonthlyShift!.Value);

    private static Shift NextMonthlyShift(Shift shift) => shift switch
    {
        Shift.Early => Shift.Afternoon,
        Shift.Afternoon => Shift.Night,
        Shift.Night => Shift.Early,
        _ => throw new ArgumentOutOfRangeException(nameof(shift))
    };

    private sealed record ModelVariables(
        Dictionary<(string Employee, DateOnly Date, Shift Shift), BoolVar> Work,
        Dictionary<(string Employee, DateOnly Date), BoolVar> Rest,
        Dictionary<(string Employee, DateOnly Date), BoolVar> SpecialRest,
        Dictionary<(string Employee, DateOnly Date), BoolVar> LeaveRest,
        Dictionary<(string Employee, DateOnly Date), BoolVar> AnyRest,
        Dictionary<(string Employee, DateOnly Date), BoolVar> ActualWork);
}
