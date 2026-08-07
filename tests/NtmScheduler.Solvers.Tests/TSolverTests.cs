using Microsoft.VisualStudio.TestTools.UnitTesting;
using NtmScheduler.Cli;

namespace NtmScheduler.Solvers.Tests;

[TestClass]
public sealed class TSolverTests
{
    [TestMethod]
    public void Solve_MonthlySchedules_ReturnsCandidateAndCreditsNewHire()
    {
        var input = ValidInput();
        var result = TSolver.Solve(input, new SolverOptions { TimeLimit = TimeSpan.FromSeconds(30) });

        Assert.AreNotEqual(SolveStatus.InvalidInput, result.Status, string.Join(Environment.NewLine, result.Errors));
        Assert.AreNotEqual(SolveStatus.Infeasible, result.Status);
        Assert.IsGreaterThanOrEqualTo(1, result.Candidates.Count);
        Assert.IsNull(result.Candidates[0].Schedule.Employees[0].EmploymentStartDate);
        var hire = result.Candidates[0].Schedule.Employees.Single(employee => employee.EmployeeId == "T-NEW");
        Assert.AreEqual(new RestUsage(12, 1), hire.OpeningUsage);
        Assert.IsGreaterThanOrEqualTo(2, hire.ClosingUsage!.Rest);
        Assert.IsGreaterThanOrEqualTo(1, hire.ClosingUsage.SpecialRest);
        Assert.IsTrue(hire.Assignments.Keys.All(date => date >= hire.EmploymentStartDate!.Value));
        CollectionAssert.AreEqual(
            new[] { "RequestedRest", "StaffingQuality", "MonthlyRestDistribution", "WorkPatternQuality", "RestFairness" },
            result.Candidates[0].Objectives.Select(value => value.Name).ToArray());
        Assert.IsGreaterThanOrEqualTo(1, result.Candidates[0].Objectives
            .SelectMany(value => value.Components)
            .Single(value => value.Name == "NightToEarlyRest").Value);
    }

    [TestMethod]
    public void Solve_MidmonthHireBeforeStartCells_ReturnInvalidInput()
    {
        var input = ValidInput();
        var hire = input.DemandMonth.Employees.Single(employee => employee.EmployeeId == "T-NEW");
        var changed = hire with
        {
            Assignments = new Dictionary<DateOnly, ScheduleCell>(hire.Assignments)
            {
                [hire.EmploymentStartDate!.Value.AddDays(-1)] = new() { Kind = AssignmentKind.Rest }
            }
        };
        input = input with
        {
            DemandMonth = input.DemandMonth with
            {
                Employees = input.DemandMonth.Employees.Select(employee => employee.EmployeeId == changed.EmployeeId ? changed : employee).ToArray()
            }
        };

        Assert.AreEqual(SolveStatus.InvalidInput, TSolver.Solve(input).Status);
    }

    [TestMethod]
    public void Csv_RoundTripSupportsBomQuotesAndCrossMidnightEvent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var input = ValidInput();
            var employee = input.DemandMonth.Employees[0] with
            {
                Name = "Quoted, \"Name\"",
                Assignments = new Dictionary<DateOnly, ScheduleCell>
                {
                    [new(2026, 9, 3)] = Event(new(2026, 9, 3), new(23, 30), new(7, 0)),
                    [new(2026, 9, 4)] = new() { RequestedRest = true },
                    [new(2026, 9, 5)] = new() { Kind = AssignmentKind.Rest, RequestedRest = true }
                }
            };
            var schedule = input.DemandMonth with { Employees = [employee] };
            var path = Path.Combine(root, "schedule.csv");

            ScheduleCsv.WriteMonthly(path, schedule);
            var parsed = ScheduleCsv.ReadMonthly(path, schedule.MonthStart);

            Assert.AreEqual(employee.Name, parsed.Employees[0].Name);
            Assert.IsNull(parsed.Employees[0].EmploymentStartDate);
            Assert.AreEqual(new DateOnly(2026, 9, 4), DateOnly.FromDateTime(parsed.Employees[0].Assignments[new(2026, 9, 3)].EventEnd!.Value.Date));
            Assert.AreEqual(0xEF, File.ReadAllBytes(path)[0]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Csv_RejectsNonexistentDayAndInvalidCell()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "schedule.csv");
            ScheduleCsv.WriteMonthly(path, ValidInput().DemandMonth);
            var text = File.ReadAllText(path).Replace(",,,\r\n", ",,,\r\n", StringComparison.Ordinal);
            var lines = text.Split('\n');
            var fields = lines[1].TrimEnd('\r').Split(',');
            fields[38] = "BAD";
            lines[1] = string.Join(',', fields);
            File.WriteAllText(path, string.Join('\n', lines));

            Assert.ThrowsExactly<ScheduleCsvException>(() => ScheduleCsv.ReadMonthly(path, new(2026, 9, 1)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow("m-2026-09", true, false)]
    [DataRow("t-2026-09", false, false)]
    [DataRow("t-2026-09", false, true)]
    public void Cli_Example_RedirectedInputWritesCandidate(string example, bool expectsExternal, bool declineOverwrite)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var exampleRoot = Path.Combine(root, "examples", example);
        var outputRoot = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalInput = Console.In;
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        try
        {
            Directory.SetCurrentDirectory(outputRoot);
            if (declineOverwrite) File.WriteAllText(Path.Combine(outputRoot, "candidate-1.csv"), "keep");
            Console.SetIn(new StringReader(string.Join('\n',
                "2026-09",
                Path.Combine(exampleRoot, "previous.csv"),
                Path.Combine(exampleRoot, "demand.csv"),
                Path.Combine(exampleRoot, "rest-intervals.csv"),
                declineOverwrite ? "n" : "") + "\n"));
            var output = new StringWriter();
            var error = new StringWriter();
            Console.SetOut(output);
            Console.SetError(error);

            Assert.AreEqual(0, Program.Main(), output + Environment.NewLine + error);
            Assert.IsTrue(File.Exists(Path.Combine(outputRoot, "candidate-1.csv")));
            Assert.AreEqual(expectsExternal, File.Exists(Path.Combine(outputRoot, "candidate-1-external.csv")));
            if (declineOverwrite) Assert.AreEqual("keep", File.ReadAllText(Path.Combine(outputRoot, "candidate-1.csv")));
        }
        finally
        {
            Console.SetIn(originalInput);
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(outputRoot, true);
        }
    }

    [TestMethod]
    public void Solve_PreCanceledToken_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => TSolver.Solve(ValidInput(), cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public void Solve_BlankAndDatedEmploymentStarts_ReturnInvalidInput()
    {
        var input = ValidInput();
        var first = input.PreviousMonth.Employees[0] with { EmploymentStartDate = new(2020, 1, 1) };
        input = input with
        {
            PreviousMonth = input.PreviousMonth with
            {
                Employees = [first, .. input.PreviousMonth.Employees.Skip(1)]
            }
        };

        var result = TSolver.Solve(input);

        Assert.AreEqual(SolveStatus.InvalidInput, result.Status);
        Assert.IsTrue(result.Errors.Any(error => error.Field == "EmploymentStartDate"));
    }

    internal static ScheduleInput ValidInput()
    {
        var month = new DateOnly(2026, 9, 1);
        var first = new RestInterval(new(2026, 7, 20), new(2026, 9, 13), new HashSet<DateOnly> { new(2026, 8, 14) });
        var second = new RestInterval(new(2026, 9, 14), new(2026, 11, 8), new HashSet<DateOnly> { new(2026, 9, 18) });
        var previous = new List<EmployeeMonthlySchedule>();
        var demand = new List<EmployeeMonthlySchedule>();
        var definitions = new[]
        {
            ("T-E1", "Electrical", 5, Shift.Early, Shift.Night),
            ("T-E2", "Track", 3, Shift.Early, Shift.Early),
            ("T-A1", "Electrical", 4, Shift.Afternoon, Shift.Afternoon),
            ("T-A2", "Track", 2, Shift.Afternoon, Shift.Afternoon),
            ("T-N1", "Electrical", 5, Shift.Night, Shift.Night),
            ("T-N2", "Track", 3, Shift.Night, Shift.Night)
        };

        foreach (var (id, group, ability, currentShift, priorShift) in definitions)
        {
            var history = Enumerable.Range(0, 31).ToDictionary(
                day => new DateOnly(2026, 8, 1).AddDays(day),
                day => day % 6 == 5
                    ? new ScheduleCell { Kind = AssignmentKind.Rest }
                    : new ScheduleCell { Kind = AssignmentKind.Work, Shift = priorShift });
            var closing = new RestUsage(12, 1);
            previous.Add(Row(id, group, ability, priorShift, null, history, null, closing, 26));
            var assignments = new Dictionary<DateOnly, ScheduleCell>();
            if (id == "T-E1")
            {
                assignments[month] = new() { Kind = AssignmentKind.Rest };
                assignments[month.AddDays(1)] = new() { Kind = AssignmentKind.Work, Shift = Shift.Early };
            }
            if (id == "T-E2") assignments[month.AddDays(4)] = new() { RequestedRest = true };
            if (id == "T-A1") assignments[month.AddDays(7)] = Event(month.AddDays(7), new(8, 30), new(17, 30));
            demand.Add(Row(id, group, ability, currentShift, null, assignments, closing, null, null));
        }

        demand.Add(Row("T-NEW", "Signal", 4, Shift.Early, new(2026, 9, 21), new Dictionary<DateOnly, ScheduleCell>(), new(12, 1), null, null));
        return new(new(month.AddMonths(-1), previous), new(month, demand), [first, second]);
    }

    private static ScheduleCell Event(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var offset = TimeSpan.FromHours(8);
        return new()
        {
            Kind = AssignmentKind.WorkEvent,
            EventStart = new DateTimeOffset(date.ToDateTime(start), offset),
            EventEnd = new DateTimeOffset((end <= start ? date.AddDays(1) : date).ToDateTime(end), offset)
        };
    }

    private static EmployeeMonthlySchedule Row(
        string id,
        string affiliation,
        int ability,
        Shift shift,
        DateOnly? start,
        IReadOnlyDictionary<DateOnly, ScheduleCell> assignments,
        RestUsage? opening,
        RestUsage? closing,
        int? workCount) => new()
        {
            EmployeeId = id,
            Name = $"T Employee {id}",
            Affiliation = affiliation,
            EmploymentStartDate = start,
            Ability = ability,
            MonthlyShift = shift,
            Assignments = assignments,
            OpeningUsage = opening,
            ClosingUsage = closing,
            NormalWorkCount = workCount
        };

}
