using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NtmScheduler.Solvers.Tests;

internal static class SolverAcceptanceAssertions
{
    private static readonly string[] Stations = Enumerable.Range(1, 12).Select(value => $"LB{value:D2}").ToArray();
    private static readonly Shift[] Shifts = [Shift.Early, Shift.Afternoon, Shift.Night];

    internal static void AssertMHardRules(ScheduleInput input, MCandidate candidate)
    {
        AssertCommonHardRules(input, candidate.Schedule, false);
        var demand = input.DemandMonth.Employees.ToDictionary(employee => employee.EmployeeId);
        var assigned = candidate.Schedule.Employees.SelectMany(employee => employee.Assignments.Select(pair =>
            (Employee: employee.EmployeeId, pair.Key, Cell: pair.Value)));

        foreach (var item in assigned.Where(item => item.Cell.Kind == AssignmentKind.Work))
        {
            var home = demand[item.Employee].Affiliation;
            Assert.AreEqual(StationGroup(home), StationGroup(item.Cell.Station!), $"{item.Employee}/{item.Key:yyyy-MM-dd} works outside its three-station group.");
        }

        foreach (var date in TargetDates(input))
        foreach (var station in Stations)
        foreach (var shift in Shifts)
        {
            var employeeCount = assigned.Count(item => item.Key == date && item.Cell.Kind == AssignmentKind.Work && item.Cell.Station == station && item.Cell.Shift == shift);
            var externalCount = candidate.ExternalAssignments.Where(item => item.Date == date && item.Station == station && item.Shift == shift).Sum(item => item.Count);
            var required = shift is Shift.Early or Shift.Afternoon || shift == Shift.Night && station is "LB01" or "LB06" or "LB08" or "LB12" ? 1 : 0;
            Assert.AreEqual(required, employeeCount + externalCount, $"Coverage mismatch for {date:yyyy-MM-dd}/{station}/{shift}.");
        }

        Assert.IsTrue(candidate.ExternalAssignments.All(item => item.Station is "LB02" or "LB04" or "LB11"));
    }

    internal static void AssertTHardRules(ScheduleInput input, TCandidate candidate)
    {
        AssertCommonHardRules(input, candidate.Schedule, true);
        var demand = input.DemandMonth.Employees.ToDictionary(employee => employee.EmployeeId);
        foreach (var employee in candidate.Schedule.Employees)
        foreach (var cell in employee.Assignments.Values.Where(cell => cell.Kind == AssignmentKind.Work))
            Assert.AreEqual(demand[employee.EmployeeId].MonthlyShift, cell.Shift, $"{employee.EmployeeId} works outside T月班別.");
    }

    internal static void AssertMSoftRules(ScheduleInput input, MCandidate candidate)
    {
        AssertObjectiveStructure(candidate.Objectives,
        [
            (1, "RequestedRest", [("RequestedRest", 1)]),
            (2, "ExternalStaffing", [("ExternalStaffing", 1)]),
            (3, "MonthlyRestDistribution", [("MonthlyRest", 4), ("MonthlySpecialRest", 8)]),
            (4, "ScheduleQuality", [("NonHomeStation", 8), ("WorkStreak", 3), ("SameShiftBlock", 2), ("NightRestEarly", 12), ("NightRestAfternoon", 8), ("ShiftChangeWithoutRest", 6)]),
            (5, "RotationAndFairness", [("NonPreferredRotation", 1), ("WeekdayRestFairness", 2), ("HolidayRestFairness", 4), ("SupportFairness", 3), ("EarlyShiftFairness", 1), ("AfternoonShiftFairness", 1), ("NightShiftFairness", 2)])
        ]);

        Expect(candidate.Objectives, "RequestedRest", RequestedRestViolations(input, candidate.Schedule));
        Expect(candidate.Objectives, "ExternalStaffing", candidate.ExternalAssignments.Sum(item => item.Count));
        Expect(candidate.Objectives, "MonthlyRest", MonthlyRestPenalty(input, candidate.Schedule, AssignmentKind.Rest));
        Expect(candidate.Objectives, "MonthlySpecialRest", MonthlyRestPenalty(input, candidate.Schedule, AssignmentKind.SpecialRest));
        Expect(candidate.Objectives, "NonHomeStation", candidate.Schedule.Employees.Sum(employee => employee.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.Work && cell.Station != employee.Affiliation)));

        var completedStreakPenalty = CompletedWorkStreakPenalty(input, candidate.Schedule);
        Assert.IsGreaterThanOrEqualTo(completedStreakPenalty, Component(candidate.Objectives, "WorkStreak").Value);
        Assert.IsGreaterThan(0, completedStreakPenalty);
        Expect(candidate.Objectives, "SameShiftBlock", SameShiftBlockPenalty(candidate.Schedule));

        var earlyPatterns = ObservableNightRestPatterns(input, candidate.Schedule, Shift.Early);
        var afternoonPatterns = ObservableNightRestPatterns(input, candidate.Schedule, Shift.Afternoon);
        Assert.IsGreaterThanOrEqualTo(earlyPatterns, Component(candidate.Objectives, "NightRestEarly").Value);
        Assert.IsGreaterThanOrEqualTo(afternoonPatterns, Component(candidate.Objectives, "NightRestAfternoon").Value);
        Assert.IsGreaterThan(0, earlyPatterns);
        Assert.IsGreaterThan(0, afternoonPatterns);

        Expect(candidate.Objectives, "ShiftChangeWithoutRest", ShiftChangesWithoutRest(input, candidate.Schedule));
        Expect(candidate.Objectives, "NonPreferredRotation", NonPreferredRotations(input, candidate.Schedule));
        Expect(candidate.Objectives, "WeekdayRestFairness", MRestFairness(input, candidate.Schedule, false));
        Expect(candidate.Objectives, "HolidayRestFairness", MRestFairness(input, candidate.Schedule, true));
        Expect(candidate.Objectives, "SupportFairness", MSupportFairness(input, candidate.Schedule));
        Expect(candidate.Objectives, "EarlyShiftFairness", MShiftDispersion(input, candidate.Schedule, Shift.Early));
        Expect(candidate.Objectives, "AfternoonShiftFairness", MShiftDispersion(input, candidate.Schedule, Shift.Afternoon));
        Expect(candidate.Objectives, "NightShiftFairness", MShiftDispersion(input, candidate.Schedule, Shift.Night));
    }

    internal static void AssertTSoftRules(ScheduleInput input, TCandidate candidate)
    {
        AssertObjectiveStructure(candidate.Objectives,
        [
            (1, "RequestedRest", [("RequestedRest", 1)]),
            (2, "StaffingQuality", [("Attendance", 9), ("Specialty", 3), ("Ability", 1)]),
            (3, "MonthlyRestDistribution", [("MonthlyRest", 1), ("MonthlySpecialRest", 1)]),
            (4, "WorkPatternQuality", [("WorkStreak", 3), ("NightToEarlyRest", 12), ("MonthBoundaryRestBalance", 5)]),
            (5, "RestFairness", [("WeekdayRestFairness", 2), ("HolidayRestFairness", 4)])
        ]);

        Expect(candidate.Objectives, "RequestedRest", RequestedRestViolations(input, candidate.Schedule));
        Expect(candidate.Objectives, "Attendance", TAttendance(input, candidate.Schedule));
        Expect(candidate.Objectives, "Specialty", TSpecialty(input, candidate.Schedule));
        Expect(candidate.Objectives, "Ability", TAbility(input, candidate.Schedule));
        Expect(candidate.Objectives, "MonthlyRest", MonthlyRestPenalty(input, candidate.Schedule, AssignmentKind.Rest));
        Expect(candidate.Objectives, "MonthlySpecialRest", MonthlyRestPenalty(input, candidate.Schedule, AssignmentKind.SpecialRest));

        var completedStreakPenalty = CompletedWorkStreakPenalty(input, candidate.Schedule);
        Assert.IsGreaterThanOrEqualTo(completedStreakPenalty, Component(candidate.Objectives, "WorkStreak").Value);
        Assert.IsGreaterThan(0, completedStreakPenalty);
        Expect(candidate.Objectives, "NightToEarlyRest", TNightToEarly(input, candidate.Schedule));
        Expect(candidate.Objectives, "MonthBoundaryRestBalance", TBoundaryRestBalance(input, candidate.Schedule));
        Expect(candidate.Objectives, "WeekdayRestFairness", TRestFairness(input, candidate.Schedule, false));
        Expect(candidate.Objectives, "HolidayRestFairness", TRestFairness(input, candidate.Schedule, true));
    }

    internal static void AssertCandidateDifference(ScheduleInput input, IReadOnlyList<MonthlySchedule> candidates)
    {
        Assert.IsGreaterThanOrEqualTo(2, candidates.Count, "The fixture must produce alternative candidates.");
        var comparable = (from employee in input.DemandMonth.Employees
                          from date in TargetDates(input)
                          where IsActive(employee, date) && employee.Assignments.GetValueOrDefault(date)?.Kind is null
                          select (employee.EmployeeId, Date: date)).ToArray();
        var minimum = (int)Math.Ceiling(comparable.Length * 0.10);

        for (var first = 0; first < candidates.Count; first++)
        for (var second = first + 1; second < candidates.Count; second++)
        {
            var left = candidates[first].Employees.ToDictionary(employee => employee.EmployeeId);
            var right = candidates[second].Employees.ToDictionary(employee => employee.EmployeeId);
            var difference = comparable.Count(cell => Signature(left[cell.EmployeeId].Assignments[cell.Date]) != Signature(right[cell.EmployeeId].Assignments[cell.Date]));
            Assert.IsGreaterThanOrEqualTo(minimum, difference, $"Candidates {first + 1} and {second + 1} differ in only {difference}/{comparable.Length} comparable cells.");
        }
    }

    private static void AssertCommonHardRules(ScheduleInput input, MonthlySchedule schedule, bool t)
    {
        var demand = input.DemandMonth.Employees.ToDictionary(employee => employee.EmployeeId);
        foreach (var employee in schedule.Employees)
        {
            var source = demand[employee.EmployeeId];
            var activeDates = TargetDates(input).Where(date => IsActive(source, date)).ToArray();
            CollectionAssert.AreEquivalent(activeDates, employee.Assignments.Keys.ToArray(), $"{employee.EmployeeId} must have exactly one output cell per active day.");
            Assert.IsTrue(employee.Assignments.Values.All(cell => cell.Kind is not null));

            foreach (var fixedCell in source.Assignments.Where(pair => pair.Value.Kind is not null))
                AssertCell(fixedCell.Value, employee.Assignments[fixedCell.Key], $"Fixed cell changed for {employee.EmployeeId}/{fixedCell.Key:yyyy-MM-dd}.");

            var leaveDates = employee.Assignments.Where(pair => pair.Value.Kind == AssignmentKind.LeaveRest).ToArray();
            Assert.HasCount(source.RequestedLeaveRestCount ?? 0, leaveDates, $"Wrong R休 count for {employee.EmployeeId}.");
            Assert.IsTrue(leaveDates.All(pair => source.Assignments.GetValueOrDefault(pair.Key)?.RequestedRest == true), $"R休 must use R* for {employee.EmployeeId}.");

            AssertSevenDayRestWindows(input, source, employee);
            AssertClosedRestQuotas(input, source, employee);
            AssertMinimumWorkGap(input, employee, t);
        }
    }

    private static void AssertSevenDayRestWindows(ScheduleInput input, EmployeeMonthlySchedule source, EmployeeMonthlySchedule candidate)
    {
        var history = input.PreviousMonth.Employees.FirstOrDefault(employee => employee.EmployeeId == source.EmployeeId)?.Assignments;
        foreach (var end in TargetDates(input).Where(date => IsActive(source, date.AddDays(-6))))
        {
            var rests = Enumerable.Range(0, 7).Select(offset => end.AddDays(-offset)).Count(date =>
                (date < input.DemandMonth.MonthStart ? history?.GetValueOrDefault(date) : candidate.Assignments.GetValueOrDefault(date))?.Kind == AssignmentKind.Rest);
            Assert.IsGreaterThanOrEqualTo(1, rests, $"No general R in seven-day window ending {end:yyyy-MM-dd} for {source.EmployeeId}.");
        }
    }

    private static void AssertClosedRestQuotas(ScheduleInput input, EmployeeMonthlySchedule source, EmployeeMonthlySchedule employee)
    {
        var monthEnd = input.DemandMonth.MonthStart.AddMonths(1).AddDays(-1);
        foreach (var interval in input.RestIntervals.Where(interval => interval.End >= input.DemandMonth.MonthStart && interval.End <= monthEnd))
        {
            RestUsage opening;
            if (input.PreviousMonth.Employees.Any(value => value.EmployeeId == employee.EmployeeId) && interval.Start < input.DemandMonth.MonthStart)
                opening = employee.OpeningUsage!;
            else if (source.EmploymentStartDate is { } start && start > interval.Start)
            {
                var end = start.AddDays(-1) < interval.End ? start.AddDays(-1) : interval.End;
                var credited = Enumerable.Range(0, end.DayNumber - interval.Start.DayNumber + 1).Select(interval.Start.AddDays).ToArray();
                opening = new(credited.Count(date => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday), credited.Count(interval.NationalHolidays.Contains));
            }
            else opening = new(0, 0);
            var cells = employee.Assignments.Where(pair => pair.Key >= interval.Start && pair.Key <= interval.End).Select(pair => pair.Value).ToArray();
            Assert.AreEqual(16, opening.Rest + cells.Count(cell => cell.Kind == AssignmentKind.Rest), $"Wrong 56-day R quota for {employee.EmployeeId}.");
            Assert.AreEqual(interval.NationalHolidays.Count, opening.SpecialRest + cells.Count(cell => cell.Kind == AssignmentKind.SpecialRest), $"Wrong 56-day R1 quota for {employee.EmployeeId}.");
        }
    }

    private static void AssertMinimumWorkGap(ScheduleInput input, EmployeeMonthlySchedule candidate, bool t)
    {
        var history = input.PreviousMonth.Employees.FirstOrDefault(employee => employee.EmployeeId == candidate.EmployeeId)?.Assignments ?? new Dictionary<DateOnly, ScheduleCell>();
        var work = history.Concat(candidate.Assignments).Select(pair => WorkInterval(pair.Key, pair.Value, t)).Where(value => value is not null).Select(value => value!.Value).OrderBy(value => value.Start).ToArray();
        for (var index = 1; index < work.Length; index++)
            Assert.IsGreaterThanOrEqualTo(TimeSpan.FromHours(11), work[index].Start - work[index - 1].End, $"Insufficient work gap for {candidate.EmployeeId} before {work[index].Start}.");
    }

    private static void AssertObjectiveStructure(
        IReadOnlyList<ObjectiveScore> actual,
        IReadOnlyList<(int Priority, string Name, IReadOnlyList<(string Name, int Weight)> Components)> expected)
    {
        Assert.HasCount(expected.Count, actual);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.AreEqual(expected[index].Priority, actual[index].Priority);
            Assert.AreEqual(expected[index].Name, actual[index].Name);
            CollectionAssert.AreEqual(expected[index].Components.Select(item => item.Name).ToArray(), actual[index].Components.Select(item => item.Name).ToArray());
            CollectionAssert.AreEqual(expected[index].Components.Select(item => item.Weight).ToArray(), actual[index].Components.Select(item => item.Weight).ToArray());
            Assert.AreEqual(actual[index].Components.Sum(component => component.WeightedValue), actual[index].Value, $"Weighted total mismatch for {actual[index].Name}.");
        }
    }

    private static long RequestedRestViolations(ScheduleInput input, MonthlySchedule schedule)
    {
        var candidates = schedule.Employees.ToDictionary(employee => employee.EmployeeId);
        return input.DemandMonth.Employees.Sum(employee => employee.Assignments.Count(pair => pair.Value.RequestedRest && !IsRest(candidates[employee.EmployeeId].Assignments[pair.Key])));
    }

    private static long MonthlyRestPenalty(ScheduleInput input, MonthlySchedule schedule, AssignmentKind kind)
    {
        var demand = input.DemandMonth.Employees.ToDictionary(employee => employee.EmployeeId);
        return schedule.Employees.Sum(employee =>
        {
            var active = TargetDates(input).Where(date => IsActive(demand[employee.EmployeeId], date)).ToArray();
            var target = kind == AssignmentKind.Rest ? active.Count(date => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) : active.Count(date => IsNationalHoliday(input, date));
            var actual = employee.Assignments.Values.Count(cell => cell.Kind == kind);
            var excess = Math.Max(Math.Abs(actual - target) - 1, 0);
            return (long)excess * excess;
        });
    }

    private static long CompletedWorkStreakPenalty(ScheduleInput input, MonthlySchedule schedule)
    {
        var total = 0L;
        foreach (var employee in schedule.Employees)
        {
            var history = input.PreviousMonth.Employees.FirstOrDefault(value => value.EmployeeId == employee.EmployeeId)?.Assignments.OrderBy(pair => pair.Key).ToArray() ?? [];
            var streak = history.Reverse().TakeWhile(pair => IsWork(pair.Value)).Count();
            var dates = employee.Assignments.Keys.Order().ToArray();
            for (var index = 0; index + 1 < dates.Length; index++)
            {
                if (IsWork(employee.Assignments[dates[index]]))
                {
                    streak++;
                    if (!IsWork(employee.Assignments[dates[index + 1]])) total += BlockPenalty(streak);
                }
                else streak = 0;
            }
        }
        return total;
    }

    private static long SameShiftBlockPenalty(MonthlySchedule schedule) => schedule.Employees.Sum(employee =>
        Runs(employee.Assignments.OrderBy(pair => pair.Key).Where(pair => pair.Value.Kind == AssignmentKind.Work).Select(pair => pair.Value.Shift!.Value))
            .Sum(run => (long)BlockPenalty(run.Count)));

    private static long ObservableNightRestPatterns(ScheduleInput input, MonthlySchedule schedule, Shift finalShift)
    {
        var monthStart = input.DemandMonth.MonthStart;
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return schedule.Employees.Sum(employee => Enumerable.Range(0, monthEnd.DayNumber - monthStart.AddDays(-2).DayNumber - 1)
            .Select(monthStart.AddDays(-2).AddDays)
            .Count(date => Cell(input, schedule, employee.EmployeeId, date)?.Kind == AssignmentKind.Work
                && Cell(input, schedule, employee.EmployeeId, date)?.Shift == Shift.Night
                && IsRest(Cell(input, schedule, employee.EmployeeId, date.AddDays(1)))
                && Cell(input, schedule, employee.EmployeeId, date.AddDays(2))?.Kind == AssignmentKind.Work
                && Cell(input, schedule, employee.EmployeeId, date.AddDays(2))?.Shift == finalShift));
    }

    private static long ShiftChangesWithoutRest(ScheduleInput input, MonthlySchedule schedule)
    {
        var total = 0L;
        foreach (var employee in schedule.Employees)
        {
            Shift? previous = null;
            foreach (var cell in History(input, employee.EmployeeId))
            {
                if (IsRest(cell)) previous = null;
                else if (cell.Kind == AssignmentKind.Work) previous = cell.Shift;
            }
            foreach (var cell in employee.Assignments.OrderBy(pair => pair.Key).Select(pair => pair.Value))
            {
                if (IsRest(cell)) previous = null;
                else if (cell.Kind == AssignmentKind.Work)
                {
                    if (previous is not null && previous != cell.Shift) total++;
                    previous = cell.Shift;
                }
            }
        }
        return total;
    }

    private static long NonPreferredRotations(ScheduleInput input, MonthlySchedule schedule)
    {
        HashSet<(Shift, Shift)> preferred = [(Shift.Early, Shift.Afternoon), (Shift.Afternoon, Shift.Night), (Shift.Night, Shift.Early)];
        var total = 0L;
        foreach (var employee in schedule.Employees)
        {
            Shift? previous = History(input, employee.EmployeeId).LastOrDefault(cell => cell.Kind == AssignmentKind.Work)?.Shift;
            foreach (var cell in employee.Assignments.OrderBy(pair => pair.Key).Select(pair => pair.Value).Where(cell => cell.Kind == AssignmentKind.Work))
            {
                if (previous is not null && previous != cell.Shift && !preferred.Contains((previous.Value, cell.Shift!.Value))) total++;
                previous = cell.Shift;
            }
        }
        return total;
    }

    private static long MRestFairness(ScheduleInput input, MonthlySchedule schedule, bool holiday) => GroupRange(
        input.DemandMonth.Employees.Where(employee => IsActive(employee, input.DemandMonth.MonthStart)).GroupBy(employee => StationGroup(employee.Affiliation)),
        schedule,
        employee => employee.Assignments.Count(pair => IsHoliday(input, pair.Key) == holiday && IsRest(pair.Value)));

    private static long MSupportFairness(ScheduleInput input, MonthlySchedule schedule) => GroupRange(
        input.DemandMonth.Employees.Where(employee => IsActive(employee, input.DemandMonth.MonthStart)).GroupBy(employee => StationGroup(employee.Affiliation)),
        schedule,
        employee => employee.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.Work && cell.Station != employee.Affiliation));

    private static long MShiftDispersion(ScheduleInput input, MonthlySchedule schedule, Shift shift)
    {
        var candidates = schedule.Employees.ToDictionary(employee => employee.EmployeeId);
        return input.DemandMonth.Employees.Where(employee => IsActive(employee, input.DemandMonth.MonthStart)).GroupBy(employee => StationGroup(employee.Affiliation)).Sum(group =>
        {
            var counts = group.Select(employee => (long)candidates[employee.EmployeeId].Assignments.Values.Count(cell => cell.Kind == AssignmentKind.Work && cell.Shift == shift)).ToArray();
            return counts.Length * counts.Sum(count => count * count) - counts.Sum() * counts.Sum();
        });
    }

    private static long TAttendance(ScheduleInput input, MonthlySchedule schedule)
    {
        var candidates = schedule.Employees.ToDictionary(employee => employee.EmployeeId);
        return TargetDates(input).Sum(date => Shifts.Sum(shift =>
        {
            var members = input.DemandMonth.Employees.Where(employee => IsActive(employee, date) && employee.MonthlyShift == shift).ToArray();
            var attendance = members.Count(employee => candidates[employee.EmployeeId].Assignments.GetValueOrDefault(date)?.Kind == AssignmentKind.Work);
            return Math.Max(members.Length / 2 - attendance, 0);
        }));
    }

    private static long TSpecialty(ScheduleInput input, MonthlySchedule schedule)
    {
        var candidates = schedule.Employees.ToDictionary(employee => employee.EmployeeId);
        return TargetDates(input).Sum(date => Shifts.Sum(shift =>
        {
            var members = input.DemandMonth.Employees.Where(employee => IsActive(employee, date) && employee.MonthlyShift == shift).ToArray();
            return members.Select(employee => employee.Affiliation).Distinct().Count(specialty =>
                !members.Any(employee => employee.Affiliation == specialty && candidates[employee.EmployeeId].Assignments.GetValueOrDefault(date)?.Kind == AssignmentKind.Work));
        }));
    }

    private static long TAbility(ScheduleInput input, MonthlySchedule schedule)
    {
        var candidates = schedule.Employees.ToDictionary(employee => employee.EmployeeId);
        return TargetDates(input).Sum(date => Shifts.Sum(shift =>
        {
            var working = input.DemandMonth.Employees.Where(employee => IsActive(employee, date) && employee.MonthlyShift == shift && candidates[employee.EmployeeId].Assignments.GetValueOrDefault(date)?.Kind == AssignmentKind.Work).ToArray();
            return Math.Max(3 * working.Length - working.Sum(employee => employee.Ability!.Value), 0);
        }));
    }

    private static long TNightToEarly(ScheduleInput input, MonthlySchedule schedule)
    {
        var candidates = schedule.Employees.ToDictionary(employee => employee.EmployeeId);
        return input.DemandMonth.Employees.Where(employee => employee.MonthlyShift == Shift.Early).Sum(employee =>
        {
            var history = input.PreviousMonth.Employees.FirstOrDefault(value => value.EmployeeId == employee.EmployeeId)?.Assignments.OrderBy(pair => pair.Key).ToArray() ?? [];
            var lastNight = history.LastOrDefault(pair => pair.Value.Kind == AssignmentKind.Work && pair.Value.Shift == Shift.Night);
            if (lastNight.Value is null) return 0;
            var firstEarly = candidates[employee.EmployeeId].Assignments.OrderBy(pair => pair.Key).FirstOrDefault(pair => pair.Value.Kind == AssignmentKind.Work);
            if (firstEarly.Value is null) return 0;
            var rests = history.Count(pair => pair.Key > lastNight.Key && IsRest(pair.Value)) + candidates[employee.EmployeeId].Assignments.Count(pair => pair.Key < firstEarly.Key && IsRest(pair.Value));
            return Math.Max(2 - rests, 0);
        });
    }

    private static long TBoundaryRestBalance(ScheduleInput input, MonthlySchedule schedule)
    {
        var previousDate = input.DemandMonth.MonthStart.AddDays(-1);
        var transitioning = input.DemandMonth.Employees.Where(employee => employee.MonthlyShift == Shift.Early && History(input, employee.EmployeeId).Any(cell => cell.Kind == AssignmentKind.Work && cell.Shift == Shift.Night)).ToArray();
        var previous = transitioning.Count(employee => IsRest(input.PreviousMonth.Employees.Single(value => value.EmployeeId == employee.EmployeeId).Assignments[previousDate]));
        var current = transitioning.Count(employee => IsRest(schedule.Employees.Single(value => value.EmployeeId == employee.EmployeeId).Assignments[input.DemandMonth.MonthStart]));
        return Math.Abs(previous - current);
    }

    private static long TRestFairness(ScheduleInput input, MonthlySchedule schedule, bool holiday) => GroupRange(
        input.DemandMonth.Employees.Where(employee => IsActive(employee, input.DemandMonth.MonthStart)).GroupBy(employee => employee.MonthlyShift),
        schedule,
        employee => employee.Assignments.Count(pair => IsHoliday(input, pair.Key) == holiday && IsRest(pair.Value)));

    private static long GroupRange<TKey>(IEnumerable<IGrouping<TKey, EmployeeMonthlySchedule>> groups, MonthlySchedule schedule, Func<EmployeeMonthlySchedule, int> count)
    {
        var candidates = schedule.Employees.ToDictionary(employee => employee.EmployeeId);
        return groups.Sum(group =>
        {
            var counts = group.Select(employee => count(candidates[employee.EmployeeId])).ToArray();
            return counts.Length < 2 ? 0 : counts.Max() - counts.Min();
        });
    }

    private static IEnumerable<(Shift Shift, int Count)> Runs(IEnumerable<Shift> shifts)
    {
        using var values = shifts.GetEnumerator();
        if (!values.MoveNext()) yield break;
        var shift = values.Current;
        var count = 1;
        while (values.MoveNext())
        {
            if (values.Current == shift) count++;
            else
            {
                yield return (shift, count);
                shift = values.Current;
                count = 1;
            }
        }
        yield return (shift, count);
    }

    private static IEnumerable<ScheduleCell> History(ScheduleInput input, string employeeId) =>
        input.PreviousMonth.Employees.FirstOrDefault(employee => employee.EmployeeId == employeeId)?.Assignments.OrderBy(pair => pair.Key).Select(pair => pair.Value) ?? [];

    private static ScheduleCell? Cell(ScheduleInput input, MonthlySchedule schedule, string employeeId, DateOnly date) => date < input.DemandMonth.MonthStart
        ? input.PreviousMonth.Employees.FirstOrDefault(employee => employee.EmployeeId == employeeId)?.Assignments.GetValueOrDefault(date)
        : schedule.Employees.Single(employee => employee.EmployeeId == employeeId).Assignments.GetValueOrDefault(date);

    private static (DateTimeOffset Start, DateTimeOffset End)? WorkInterval(DateOnly date, ScheduleCell cell, bool t)
    {
        if (cell.Kind == AssignmentKind.WorkEvent) return (cell.EventStart!.Value, cell.EventEnd!.Value);
        if (cell.Kind != AssignmentKind.Work) return null;
        var (start, end, overnight) = (t, cell.Shift) switch
        {
            (true, Shift.Early) => (new TimeOnly(7, 0), new TimeOnly(15, 0), false),
            (true, Shift.Afternoon) => (new TimeOnly(15, 0), new TimeOnly(23, 0), false),
            (true, Shift.Night) => (new TimeOnly(23, 0), new TimeOnly(7, 0), true),
            (false, Shift.Early) => (new TimeOnly(6, 30), new TimeOnly(14, 30), false),
            (false, Shift.Afternoon) => (new TimeOnly(14, 20), new TimeOnly(22, 20), false),
            (false, Shift.Night) => (new TimeOnly(22, 0), new TimeOnly(7, 0), true),
            _ => throw new AssertFailedException("Normal work needs a shift.")
        };
        var offset = TimeSpan.FromHours(8);
        return (new DateTimeOffset(date.ToDateTime(start), offset), new DateTimeOffset(date.AddDays(overnight ? 1 : 0).ToDateTime(end), offset));
    }

    private static void AssertCell(ScheduleCell expected, ScheduleCell actual, string message)
    {
        Assert.AreEqual(expected.Kind, actual.Kind, message);
        Assert.AreEqual(expected.Station, actual.Station, message);
        Assert.AreEqual(expected.Shift, actual.Shift, message);
        Assert.AreEqual(expected.EventStart, actual.EventStart, message);
        Assert.AreEqual(expected.EventEnd, actual.EventEnd, message);
    }

    private static void Expect(IReadOnlyList<ObjectiveScore> objectives, string name, long expected) =>
        Assert.AreEqual(expected, Component(objectives, name).Value, $"Wrong {name} violation count.");

    private static ObjectiveComponent Component(IReadOnlyList<ObjectiveScore> objectives, string name) =>
        objectives.SelectMany(objective => objective.Components).Single(component => component.Name == name);

    private static IEnumerable<DateOnly> TargetDates(ScheduleInput input) => Enumerable.Range(0, DateTime.DaysInMonth(input.DemandMonth.MonthStart.Year, input.DemandMonth.MonthStart.Month)).Select(input.DemandMonth.MonthStart.AddDays);
    private static bool IsActive(EmployeeMonthlySchedule employee, DateOnly date) => employee.EmploymentStartDate is not { } start || date >= start;
    private static bool IsRest(ScheduleCell? cell) => cell?.Kind is AssignmentKind.Rest or AssignmentKind.SpecialRest or AssignmentKind.LeaveRest;
    private static bool IsWork(ScheduleCell cell) => cell.Kind is AssignmentKind.Work or AssignmentKind.WorkEvent;
    private static bool IsNationalHoliday(ScheduleInput input, DateOnly date) => input.RestIntervals.Any(interval => interval.NationalHolidays.Contains(date));
    private static bool IsHoliday(ScheduleInput input, DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || IsNationalHoliday(input, date);
    private static int StationGroup(string station) => (int.Parse(station[2..]) - 1) / 3;
    private static int BlockPenalty(int length) => length switch { 1 => 4, 2 => 2, 3 => 1, 4 or 5 => 0, _ when length >= 6 => 2 * (length - 5), _ => 0 };
    private static (AssignmentKind? Kind, string? Station, Shift? Shift) Signature(ScheduleCell cell) => (cell.Kind, cell.Station, cell.Shift);
}
