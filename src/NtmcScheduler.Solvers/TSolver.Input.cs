namespace NtmcScheduler.Solvers;

public static partial class TSolver
{
    // Input snapshot

    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);
    private static readonly TimeSpan MinimumRest = TimeSpan.FromHours(11);

    private static List<InputError> FindMissingCollections(ScheduleInput input)
    {
        var errors = new List<InputError>();
        if (input.PreviousMonth is null) errors.Add(new(nameof(input.PreviousMonth), "PreviousMonth is required."));
        if (input.DemandMonth is null) errors.Add(new(nameof(input.DemandMonth), "DemandMonth is required."));
        if (input.RestIntervals is null) errors.Add(new(nameof(input.RestIntervals), "RestIntervals is required."));
        if (input.NonStandardShifts?.Shifts is null) errors.Add(new(nameof(input.NonStandardShifts), "NonStandardShifts is required."));
        if (input.PreviousMonth?.Employees is null) errors.Add(new("PreviousMonth.Employees", "Employees is required."));
        if (input.DemandMonth?.Employees is null) errors.Add(new("DemandMonth.Employees", "Employees is required."));
        if (input.RestIntervals is not null && input.RestIntervals.Any(interval => interval?.NationalHolidays is null))
            errors.Add(new("RestIntervals.NationalHolidays", "NationalHolidays is required for every interval."));
        if (input.PreviousMonth?.Employees?.Any(employee => employee?.Assignments is null) == true)
            errors.Add(new("PreviousMonth.Assignments", "Assignments is required for every employee."));
        if (input.DemandMonth?.Employees?.Any(employee => employee?.Assignments is null) == true)
            errors.Add(new("DemandMonth.Assignments", "Assignments is required for every employee."));
        return errors;
    }

    private static ScheduleInput CopyInput(ScheduleInput input) => input with
    {
        PreviousMonth = CopySchedule(input.PreviousMonth),
        DemandMonth = CopySchedule(input.DemandMonth),
        RestIntervals = input.RestIntervals.Select(interval => interval with { NationalHolidays = interval.NationalHolidays.ToHashSet() }).ToArray(),
        NonStandardShifts = input.NonStandardShifts with { Shifts = input.NonStandardShifts.Shifts.ToArray() },
        MonthlySettings = input.MonthlySettings is null ? null : input.MonthlySettings with { MStations = input.MonthlySettings.MStations.ToArray() }
    };

    private static MonthlySchedule CopySchedule(MonthlySchedule schedule) => schedule with
    {
        Employees = schedule.Employees.Select(employee => employee with
        {
            Assignments = employee.Assignments.ToDictionary(pair => pair.Key, pair => pair.Value)
        }).ToArray()
    };

    // Validation

    private static List<InputError> ValidateInput(ScheduleInput input, SolverOptions options)
    {
        var errors = new List<InputError>();
        var monthStart = input.DemandMonth.MonthStart;
        if (monthStart.Day != 1) errors.Add(new("DemandMonth.MonthStart", "MonthStart must be the first day of a month."));
        if (input.PreviousMonth.MonthStart != monthStart.AddMonths(-1)) errors.Add(new("PreviousMonth.MonthStart", "PreviousMonth must be the calendar month immediately before DemandMonth."));
        if (options.TimeLimit <= TimeSpan.Zero) errors.Add(new("options.TimeLimit", "TimeLimit must be greater than zero."));
        if (options.WorkerCount <= 0) errors.Add(new("options.WorkerCount", "WorkerCount must be greater than zero."));
        if (input.MonthlySettings is { } monthlySettings && (monthlySettings.GeneralRestTarget < 0 || monthlySettings.SpecialRestTarget < 0))
            errors.Add(new("MonthlySettings", "Monthly R and R1 targets must be non-negative."));
        try { SolverRuleWeights.Resolve(false, options.RuleWeights); }
        catch (ArgumentException) { errors.Add(new("options.RuleWeights", "Rule weights must contain every active T rule exactly once and be non-negative.")); }
        if (monthStart.Day != 1) return errors;

        var beforeIntervals = errors.Count;
        ValidateIntervals(input, errors);
        var intervalsValid = errors.Count == beforeIntervals;
        var current = UniqueEmployees(input.DemandMonth, "DemandMonth.Employees", errors);
        var previous = UniqueEmployees(input.PreviousMonth, "PreviousMonth.Employees", errors);
        ValidateScheduleRows(input.PreviousMonth, true, errors);
        ValidateScheduleRows(input.DemandMonth, false, errors);

        foreach (var employee in current.Values)
        {
            var hasHistory = previous.TryGetValue(employee.EmployeeId, out var history);
            if (!hasHistory && (employee.EmploymentStartDate is not { } start || start < monthStart))
                errors.Add(new("PreviousMonth", $"Employee '{employee.EmployeeId}' needs previous-month history unless employment starts in the target month."));
            if (hasHistory && history!.EmploymentStartDate != employee.EmploymentStartDate)
                errors.Add(new("EmploymentStartDate", $"Employee '{employee.EmployeeId}' has inconsistent employment start dates."));

            if (intervalsValid)
            {
                var expected = OpeningRestUsage(input, employee);
                var interval = RestIntervalContaining(input, monthStart);
                if (expected.Rest is < 0 or > 16 || expected.SpecialRest < 0 || expected.SpecialRest > interval.NationalHolidays.Count)
                    errors.Add(new("PreviousMonth.ClosingUsage", $"Employee '{employee.EmployeeId}' opening usage is outside the interval quota."));
                if (employee.OpeningUsage is not null && employee.OpeningUsage != expected)
                    errors.Add(new("OpeningUsage", $"Employee '{employee.EmployeeId}' opening usage must be {expected.Rest} R and {expected.SpecialRest} R1."));
            }
        }

        ValidateFixedWorkIntervals(input, errors);
        return errors;
    }

    private static Dictionary<string, EmployeeMonthlySchedule> UniqueEmployees(MonthlySchedule schedule, string field, List<InputError> errors)
    {
        var result = new Dictionary<string, EmployeeMonthlySchedule>(StringComparer.Ordinal);
        foreach (var employee in schedule.Employees)
        {
            if (string.IsNullOrWhiteSpace(employee.EmployeeId)) errors.Add(new(field, "Employee ID cannot be blank."));
            else if (!result.TryAdd(employee.EmployeeId, employee)) errors.Add(new(field, $"Duplicate employee '{employee.EmployeeId}'."));
        }
        return result;
    }

    private static void ValidateScheduleRows(MonthlySchedule schedule, bool history, List<InputError> errors)
    {
        var days = DateTime.DaysInMonth(schedule.MonthStart.Year, schedule.MonthStart.Month);
        var monthEnd = schedule.MonthStart.AddDays(days - 1);
        foreach (var employee in schedule.Employees)
        {
            var prefix = history ? "PreviousMonth" : "DemandMonth";
            if (string.IsNullOrWhiteSpace(employee.Name)) errors.Add(new($"{prefix}.Employees", $"Employee '{employee.EmployeeId}' name cannot be blank."));
            if (string.IsNullOrWhiteSpace(employee.Affiliation)) errors.Add(new($"{prefix}.Employees", $"Employee '{employee.EmployeeId}' affiliation cannot be blank."));
            if (employee.Ability is < 1 or > 5) errors.Add(new($"{prefix}.Employees", $"Employee '{employee.EmployeeId}' ability must be between 1 and 5."));
            if (employee.MonthlyShift is null) errors.Add(new($"{prefix}.Employees", $"Employee '{employee.EmployeeId}' needs a T monthly shift."));
            if (employee.PerpetualScheduleId is not null) errors.Add(new($"{prefix}.PerpetualScheduleId", $"T employee '{employee.EmployeeId}' cannot use an M perpetual schedule."));
            if (employee.EmploymentStartDate is { } start && start > monthEnd) errors.Add(new($"{prefix}.Employees", $"Employee '{employee.EmployeeId}' starts after this schedule month."));

            foreach (var pair in employee.Assignments)
            {
                if (pair.Key < schedule.MonthStart || pair.Key > monthEnd)
                    errors.Add(new($"{prefix}.Assignments", $"Assignment {employee.EmployeeId}/{pair.Key:yyyy-MM-dd} is outside the schedule month."));
                if (employee.EmploymentStartDate is { } employmentStart && pair.Key < employmentStart)
                    errors.Add(new($"{prefix}.Assignments", $"Assignment {employee.EmployeeId}/{pair.Key:yyyy-MM-dd} is before employment starts."));
                ValidateCell(employee, pair.Key, pair.Value, history, errors, prefix);
            }

            if (history)
            {
                foreach (var date in Enumerable.Range(0, days).Select(schedule.MonthStart.AddDays).Where(date => IsEmployedOn(employee, date)))
                {
                    if (!employee.Assignments.TryGetValue(date, out var cell) || cell.Kind is null)
                        errors.Add(new("PreviousMonth.Assignments", $"Missing resolved history for {employee.EmployeeId}/{date:yyyy-MM-dd}."));
                }
                if (employee.ClosingUsage is null) errors.Add(new("PreviousMonth.ClosingUsage", $"Employee '{employee.EmployeeId}' needs closing R/R1 usage."));
                if (employee.RequestedLeaveRestCount is not null) errors.Add(new("PreviousMonth.RequestedLeaveRestCount", $"Employee '{employee.EmployeeId}' history cannot contain a requested R休 count."));
                if (employee.NormalWorkCount is null) errors.Add(new("PreviousMonth.NormalWorkCount", $"Employee '{employee.EmployeeId}' needs a normal-work count."));
                else if (employee.NormalWorkCount < 0) errors.Add(new("PreviousMonth.NormalWorkCount", $"Employee '{employee.EmployeeId}' normal-work count cannot be negative."));
            }
            else
            {
                ValidateRequestedLeaveRestCount(employee, errors, prefix);
                if (employee.ClosingUsage is not null || employee.NormalWorkCount is not null)
                    errors.Add(new("DemandMonth", $"Employee '{employee.EmployeeId}' demand row cannot contain solved closing totals."));
            }
        }
    }

    private static void ValidateCell(
        EmployeeMonthlySchedule employee,
        DateOnly date,
        ScheduleCell cell,
        bool history,
        List<InputError> errors,
        string prefix)
    {
        if (cell.Kind is null)
        {
            if (!cell.RequestedRest) errors.Add(new($"{prefix}.Assignments", $"Empty typed cell {employee.EmployeeId}/{date:yyyy-MM-dd} must be omitted."));
            if (history) errors.Add(new($"{prefix}.Assignments", $"Historical R* {employee.EmployeeId}/{date:yyyy-MM-dd} must resolve to R, R1, or R休."));
            return;
        }

        if (cell.Kind == AssignmentKind.Work)
        {
            if (cell.Shift is null || !Shifts.Contains(cell.Shift.Value) || cell.Station is not null)
                errors.Add(new($"{prefix}.Assignments", $"T work {employee.EmployeeId}/{date:yyyy-MM-dd} needs a valid shift and no station."));
            if (cell.EventStart is not null || cell.EventEnd is not null)
                errors.Add(new($"{prefix}.Assignments", $"Normal work {employee.EmployeeId}/{date:yyyy-MM-dd} cannot contain event times."));
            return;
        }

        if (cell.Kind == AssignmentKind.WorkEvent)
        {
            if (!HasValidWorkEventInterval(date, cell)) errors.Add(new($"{prefix}.Assignments", $"X {employee.EmployeeId}/{date:yyyy-MM-dd} needs a valid UTC+8 interval of at most 24 hours."));
            if (cell.Station is not null || cell.Shift is not null) errors.Add(new($"{prefix}.Assignments", $"X {employee.EmployeeId}/{date:yyyy-MM-dd} cannot contain a station or shift."));
            return;
        }

        if (!history && cell.Kind == AssignmentKind.LeaveRest && !cell.RequestedRest)
            errors.Add(new($"{prefix}.Assignments", $"R休 {employee.EmployeeId}/{date:yyyy-MM-dd} must be marked R*."));

        if (cell.Station is not null || cell.Shift is not null || cell.EventStart is not null || cell.EventEnd is not null)
            errors.Add(new($"{prefix}.Assignments", $"Rest {employee.EmployeeId}/{date:yyyy-MM-dd} contains work-only fields."));
    }

    private static void ValidateRequestedLeaveRestCount(EmployeeMonthlySchedule employee, List<InputError> errors, string prefix)
    {
        var requested = employee.RequestedLeaveRestCount ?? 0;
        var fixedCount = employee.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.LeaveRest && cell.RequestedRest);
        if (requested < fixedCount)
            errors.Add(new($"{prefix}.RequestedLeaveRestCount", $"Employee '{employee.EmployeeId}' requested R休 limit must be at least {fixedCount}."));
    }

    private static bool HasValidWorkEventInterval(DateOnly date, ScheduleCell cell) =>
        cell.EventStart is not null &&
        cell.EventEnd is not null &&
        cell.EventStart.Value.Offset == TaipeiOffset &&
        cell.EventEnd.Value.Offset == TaipeiOffset &&
        DateOnly.FromDateTime(cell.EventStart.Value.Date) == date &&
        cell.EventEnd > cell.EventStart &&
        cell.EventEnd - cell.EventStart <= TimeSpan.FromHours(24);

    private static void ValidateIntervals(ScheduleInput input, List<InputError> errors)
    {
        var ordered = input.RestIntervals.OrderBy(interval => interval.Start).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var interval = ordered[index];
            if (interval.End.DayNumber - interval.Start.DayNumber + 1 != 56)
                errors.Add(new(nameof(input.RestIntervals), $"Interval {interval.Start:yyyy-MM-dd} must contain exactly 56 days."));
            if (index > 0 && ordered[index - 1].End.AddDays(1) != interval.Start)
                errors.Add(new(nameof(input.RestIntervals), $"Intervals around {interval.Start:yyyy-MM-dd} must be contiguous."));
            foreach (var holiday in interval.NationalHolidays)
            {
                if (holiday < interval.Start || holiday > interval.End)
                    errors.Add(new(nameof(input.RestIntervals), $"Holiday {holiday:yyyy-MM-dd} is outside its interval."));
                if (holiday.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    errors.Add(new(nameof(input.RestIntervals), $"Holiday {holiday:yyyy-MM-dd} cannot be Saturday or Sunday."));
            }
        }

        foreach (var date in PlanningHorizonDates(input))
        {
            if (ordered.Count(interval => date >= interval.Start && date <= interval.End) != 1)
                errors.Add(new(nameof(input.RestIntervals), $"Modeled date {date:yyyy-MM-dd} must belong to exactly one interval."));
        }
    }

    private static void ValidateFixedWorkIntervals(ScheduleInput input, List<InputError> errors)
    {
        var times = input.StandardShiftTimes?.T;
        foreach (var employee in input.DemandMonth.Employees)
        {
            var intervals = ResolvedHistoryFor(input, employee.EmployeeId)
                .Select(item => ResolvedWorkInterval(item.Date, item.Cell, times))
                .Where(item => item is not null)
                .Select(item => (item!.Value.Start, item.Value.End, Source: $"history {item.Value.Date:yyyy-MM-dd}"))
                .Concat(employee.Assignments
                    .Where(pair => pair.Value.Kind is AssignmentKind.Work or AssignmentKind.WorkEvent)
                    .Select(pair => ResolvedWorkInterval(pair.Key, pair.Value, times))
                    .Where(item => item is not null)
                    .Select(item => (item!.Value.Start, item.Value.End, Source: $"fixed {item.Value.Date:yyyy-MM-dd}")))
                .OrderBy(item => item.Start)
                .ToArray();

            for (var index = 1; index < intervals.Length; index++)
            {
                if (OverlapsOrLeavesLessThanMinimumRest(intervals[index - 1].Start, intervals[index - 1].End, intervals[index].Start, intervals[index].End))
                    errors.Add(new("Assignments", $"Employee '{employee.EmployeeId}' has incompatible {intervals[index - 1].Source} and {intervals[index].Source}."));
            }
        }
    }

    // History and R/R1 usage

    private static bool IsEmployedOn(EmployeeMonthlySchedule employee, DateOnly date) =>
        employee.EmploymentStartDate is not { } start || date >= start;
    private static IEnumerable<(DateOnly Date, ScheduleCell Cell)> ResolvedHistoryFor(ScheduleInput input, string employeeId)
    {
        var employee = input.PreviousMonth.Employees.FirstOrDefault(value => value.EmployeeId == employeeId);
        return employee is null ? [] : employee.Assignments.Select(pair => (pair.Key, pair.Value));
    }

    private static RestInterval RestIntervalContaining(ScheduleInput input, DateOnly date) =>
        input.RestIntervals.Single(interval => date >= interval.Start && date <= interval.End);

    private static RestUsage OpeningRestUsage(ScheduleInput input, EmployeeMonthlySchedule employee)
    {
        var interval = RestIntervalContaining(input, input.DemandMonth.MonthStart);
        if (interval.Start == input.DemandMonth.MonthStart) return new(0, 0);
        var history = input.PreviousMonth.Employees.FirstOrDefault(value => value.EmployeeId == employee.EmployeeId);
        return history?.ClosingUsage ?? StandardRestCredit(interval, interval.Start, input.DemandMonth.MonthStart.AddDays(-1));
    }

    private static RestUsage RestUsageBeforeModeledDates(ScheduleInput input, EmployeeMonthlySchedule employee, RestInterval interval)
    {
        var monthStart = input.DemandMonth.MonthStart;
        var history = input.PreviousMonth.Employees.FirstOrDefault(value => value.EmployeeId == employee.EmployeeId);
        if (history is not null && interval.Start < monthStart && interval.End >= monthStart)
            return history.ClosingUsage!;

        if (employee.EmploymentStartDate is not { } start) return new(0, 0);
        var creditEnd = start.AddDays(-1);
        if (creditEnd < interval.Start) return new(0, 0);
        return StandardRestCredit(interval, interval.Start, creditEnd < interval.End ? creditEnd : interval.End);
    }

    private static RestUsage StandardRestCredit(RestInterval interval, DateOnly start, DateOnly end)
    {
        if (end < start) return new(0, 0);
        var dates = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(start.AddDays).ToArray();
        return new(
            dates.Count(date => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday),
            dates.Count(interval.NationalHolidays.Contains));
    }

    private static int ExpectedMonthlyGeneralRestCount(ScheduleInput input, EmployeeMonthlySchedule employee)
    {
        var beforeEmployment = TargetMonthDates(input).Count(date => !IsEmployedOn(employee, date) && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        var baseline = input.MonthlySettings?.GeneralRestTarget ?? TargetMonthDates(input).Count(date => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        return Math.Max(0, baseline - beforeEmployment);
    }

    // Date and time calculations

    private static IEnumerable<DateOnly> TargetMonthDates(ScheduleInput input) =>
        Enumerable.Range(
            0,
            DateTime.DaysInMonth(input.DemandMonth.MonthStart.Year, input.DemandMonth.MonthStart.Month))
        .Select(input.DemandMonth.MonthStart.AddDays);

    private static IEnumerable<DateOnly> PlanningHorizonDates(ScheduleInput input) =>
        Enumerable.Range(
            0,
            DateTime.DaysInMonth(input.DemandMonth.MonthStart.Year, input.DemandMonth.MonthStart.Month) + ExtensionDays)
        .Select(input.DemandMonth.MonthStart.AddDays);

    private static (DateTimeOffset Start, DateTimeOffset End) NormalShiftInterval(DateOnly date, Shift shift, WorkspaceShiftTimes? times = null) =>
        (times ?? WorkspaceShiftTimes.DefaultT).Resolve(date, shift);

    private static (DateOnly Date, DateTimeOffset Start, DateTimeOffset End)? ResolvedWorkInterval(DateOnly date, ScheduleCell cell, WorkspaceShiftTimes? times = null)
    {
        if (cell.Kind == AssignmentKind.Work && cell.Shift is not null)
        {
            var interval = NormalShiftInterval(date, cell.Shift.Value, times);
            return (date, interval.Start, interval.End);
        }
        if (cell.Kind == AssignmentKind.WorkEvent && cell.EventStart is not null && cell.EventEnd is not null)
            return (date, cell.EventStart.Value, cell.EventEnd.Value);
        return null;
    }

    private static bool OverlapsOrLeavesLessThanMinimumRest(
        DateTimeOffset firstStart,
        DateTimeOffset firstEnd,
        DateTimeOffset secondStart,
        DateTimeOffset secondEnd)
    {
        if (secondStart < firstStart)
        {
            (firstStart, secondStart) = (secondStart, firstStart);
            (firstEnd, secondEnd) = (secondEnd, firstEnd);
        }
        return secondStart < firstEnd || secondStart - firstEnd < MinimumRest;
    }

    private static int HistoricalWorkStreakLength(ScheduleInput input, string employeeId)
    {
        var count = 0;
        foreach (var item in ResolvedHistoryFor(input, employeeId).OrderByDescending(item => item.Date))
        {
            if (item.Cell.Kind is not (AssignmentKind.Work or AssignmentKind.WorkEvent)) break;
            count++;
        }
        return count;
    }
}
