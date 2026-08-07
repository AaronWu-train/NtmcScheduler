using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace NtmScheduler.Solvers.Tests;

[TestClass]
public sealed class MSolverTests
{
    [TestMethod]
    public void Solve_MonthlySchedules_ReturnsNamedCandidate()
    {
        var result = MSolver.Solve(ValidInput(), new SolverOptions { TimeLimit = TimeSpan.FromSeconds(45) });

        Assert.AreNotEqual(SolveStatus.InvalidInput, result.Status, string.Join(Environment.NewLine, result.Errors));
        Assert.AreNotEqual(SolveStatus.Infeasible, result.Status);
        Assert.IsGreaterThanOrEqualTo(1, result.Candidates.Count);
        CollectionAssert.AreEqual(
            new[] { "RequestedRest", "ExternalStaffing", "MonthlyRestDistribution", "ScheduleQuality", "RotationAndFairness" },
            result.Candidates[0].Objectives.Select(value => value.Name).ToArray());
        Assert.AreEqual(new DateOnly(2026, 9, 1), result.Candidates[0].Schedule.MonthStart);
        Assert.HasCount(40, result.Candidates[0].Schedule.Employees);
        Assert.IsNull(result.Candidates[0].Schedule.Employees[0].EmploymentStartDate);
        Assert.IsGreaterThanOrEqualTo(1, result.Candidates[0].Objectives
            .SelectMany(value => value.Components)
            .Single(value => value.Name == "NightRestEarly").Value);
    }

    [TestMethod]
    public void Solve_OldEmployeeWithoutHistory_ReturnsInvalidInput()
    {
        var input = ValidInput();
        input = input with { PreviousMonth = input.PreviousMonth with { Employees = input.PreviousMonth.Employees.Skip(1).ToArray() } };

        var result = MSolver.Solve(input);

        Assert.AreEqual(SolveStatus.InvalidInput, result.Status);
        Assert.IsTrue(result.Errors.Any(error => error.Field == "PreviousMonth"));
    }

    [TestMethod]
    public void Solve_InvalidRestIntervalAndWeekendHoliday_ReturnsInvalidInput()
    {
        var input = ValidInput();
        input = input with
        {
            RestIntervals =
            [
                input.RestIntervals[0] with { End = input.RestIntervals[0].End.AddDays(-1) },
                input.RestIntervals[1] with { NationalHolidays = new HashSet<DateOnly> { new(2026, 9, 19) } }
            ]
        };

        var result = MSolver.Solve(input);

        Assert.AreEqual(SolveStatus.InvalidInput, result.Status);
        Assert.IsGreaterThanOrEqualTo(2, result.Errors.Count(error => error.Field == nameof(ScheduleInput.RestIntervals)));
    }

    [TestMethod]
    public void Solve_PreCanceledToken_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => MSolver.Solve(ValidInput(), cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public void Solve_ExpiredBudget_ReturnsTimeLimit()
    {
        var result = MSolver.Solve(ValidInput(), new SolverOptions { TimeLimit = TimeSpan.FromTicks(1) });
        Assert.AreEqual(SolveStatus.TimeLimit, result.Status);
    }

    internal static ScheduleInput ValidInput()
    {
        var month = new DateOnly(2026, 9, 1);
        var firstInterval = new RestInterval(new(2026, 7, 20), new(2026, 9, 13), new HashSet<DateOnly> { new(2026, 8, 14) });
        var secondInterval = new RestInterval(new(2026, 9, 14), new(2026, 11, 8), new HashSet<DateOnly> { new(2026, 9, 18) });
        var previous = new List<EmployeeMonthlySchedule>();
        var demand = new List<EmployeeMonthlySchedule>();
        string[] homes = ["LB01", "LB04", "LB07", "LB10"];

        for (var group = 0; group < homes.Length; group++)
        for (var index = 0; index < 10; index++)
        {
            var id = $"M{group + 1}-{index + 1:D2}";
            var targetAssignments = new Dictionary<DateOnly, ScheduleCell>();
            foreach (var day in Enumerable.Range(0, 30))
            {
                var date = month.AddDays(day);
                if (!IsRestDay(day, index)) continue;
                targetAssignments[date] = new()
                {
                    Kind = AssignmentKind.Rest,
                    RequestedRest = group == 0 && index == 0 && day == 0
                };
            }
            if (group == 0 && index == 0) targetAssignments[month.AddDays(1)] = Event(month.AddDays(1));
            if (group == 0 && index == 1) targetAssignments[month] = new() { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Early };

            var firstIntervalRests = targetAssignments.Count(pair => pair.Key <= firstInterval.End && pair.Value.Kind == AssignmentKind.Rest);
            var closing = new RestUsage(16 - firstIntervalRests, 1);
            var historyAssignments = Enumerable.Range(0, 31).ToDictionary(
                day => new DateOnly(2026, 8, 1).AddDays(day),
                day => IsRestDay(day, index)
                    ? new ScheduleCell { Kind = AssignmentKind.Rest }
                    : new ScheduleCell { Kind = AssignmentKind.Work, Station = homes[group], Shift = Shift.Early });
            if (group == 0 && index == 1)
            {
                historyAssignments[new(2026, 8, 30)] = new() { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Night };
                historyAssignments[new(2026, 8, 31)] = new() { Kind = AssignmentKind.Rest };
            }

            previous.Add(Row(id, $"M Employee {id}", homes[group], null, historyAssignments, null, closing, historyAssignments.Count(pair => pair.Value.Kind == AssignmentKind.Work)));
            demand.Add(Row(id, $"M Employee {id}", homes[group], null, targetAssignments, closing, null, null));
        }

        return new(
            new MonthlySchedule(month.AddMonths(-1), previous),
            new MonthlySchedule(month, demand),
            [firstInterval, secondInterval]);
    }

    private static bool IsRestDay(int day, int employee) => Mod(day - employee, 10) is 0 or 3 or 6;
    private static int Mod(int value, int divisor) => (value % divisor + divisor) % divisor;

    private static ScheduleCell Event(DateOnly date)
    {
        var offset = TimeSpan.FromHours(8);
        return new()
        {
            Kind = AssignmentKind.WorkEvent,
            EventStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 30)), offset),
            EventEnd = new DateTimeOffset(date.ToDateTime(new TimeOnly(17, 30)), offset)
        };
    }

    private static EmployeeMonthlySchedule Row(
        string id,
        string name,
        string affiliation,
        DateOnly? start,
        IReadOnlyDictionary<DateOnly, ScheduleCell> assignments,
        RestUsage? opening,
        RestUsage? closing,
        int? workCount) => new()
        {
            EmployeeId = id,
            Name = name,
            Affiliation = affiliation,
            EmploymentStartDate = start,
            Assignments = assignments,
            OpeningUsage = opening,
            ClosingUsage = closing,
            NormalWorkCount = workCount
        };
}
