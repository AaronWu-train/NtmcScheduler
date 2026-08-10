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
        var leaveEmployee = result.Candidates[0].Schedule.Employees.Single(employee => employee.EmployeeId == "T-E2");
        Assert.AreEqual(1, leaveEmployee.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.LeaveRest && cell.RequestedRest));
        Assert.IsNull(leaveEmployee.RequestedLeaveRestCount);
        Assert.AreEqual(16, leaveEmployee.OpeningUsage!.Rest + leaveEmployee.Assignments
            .Count(pair => pair.Key <= input.RestIntervals[0].End && pair.Value.Kind == AssignmentKind.Rest));
        Assert.AreEqual(input.RestIntervals[0].NationalHolidays.Count, leaveEmployee.OpeningUsage.SpecialRest + leaveEmployee.Assignments
            .Count(pair => pair.Key <= input.RestIntervals[0].End && pair.Value.Kind == AssignmentKind.SpecialRest));
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
        SolverAcceptanceAssertions.AssertTHardRules(input, result.Candidates[0]);
        SolverAcceptanceAssertions.AssertTSoftRules(input, result.Candidates[0]);
    }

    [TestMethod]
    public void Solve_AlternativeCandidatesDifferByTenPercent()
    {
        var input = ValidInput();
        input = input with
        {
            PreviousMonth = input.PreviousMonth with { Employees = input.PreviousMonth.Employees.Where(employee => employee.EmployeeId == "T-E2").ToArray() },
            DemandMonth = input.DemandMonth with { Employees = input.DemandMonth.Employees.Where(employee => employee.EmployeeId == "T-E2").ToArray() }
        };

        var result = TSolver.Solve(input, new SolverOptions { TimeLimit = TimeSpan.FromSeconds(10) });

        Assert.AreEqual(SolveStatus.Optimal, result.Status);
        SolverAcceptanceAssertions.AssertCandidateDifference(input, result.Candidates.Select(candidate => candidate.Schedule).ToArray());
    }

    [TestMethod]
    public void Solve_LeaveRestLimitAboveRequestedDates_ReportsUnusedAmount()
    {
        var input = ValidInput();
        var employees = input.DemandMonth.Employees.Select(employee =>
            employee.EmployeeId == "T-E2" ? employee with { RequestedLeaveRestCount = 2 } : employee).ToArray();
        input = input with { DemandMonth = input.DemandMonth with { Employees = employees } };

        var result = TSolver.Solve(input, new SolverOptions { TimeLimit = TimeSpan.FromSeconds(30) });

        Assert.IsGreaterThanOrEqualTo(1, result.Candidates.Count, string.Join(Environment.NewLine, result.Errors));
        Assert.AreEqual(1, result.Candidates[0].Objectives.SelectMany(group => group.Components)
            .Single(component => component.Name == "UnusedLeaveRest").Value);
    }

    [TestMethod]
    [DataRow(Shift.Early, Shift.Afternoon)]
    [DataRow(Shift.Afternoon, Shift.Night)]
    [DataRow(Shift.Night, Shift.Early)]
    public void Solve_ExtensionUsesNextMonthlyShift(Shift current, Shift next)
    {
        var employee = ValidInput().DemandMonth.Employees[0] with { MonthlyShift = current };
        var method = typeof(TSolver).GetMethod("MonthlyShiftOnDate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.AreEqual(current, method.Invoke(null, [employee, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1)]));
        Assert.AreEqual(next, method.Invoke(null, [employee, new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1)]));
    }

    [TestMethod]
    public void Solve_TooManyRestsInClosedInterval_ReturnsInfeasible()
    {
        var input = OnlyEmployee(ValidInput(), "T-E2");
        var employee = input.DemandMonth.Employees[0] with
        {
            Assignments = Enumerable.Range(0, 5).ToDictionary(offset => input.DemandMonth.MonthStart.AddDays(offset), _ => new ScheduleCell { Kind = AssignmentKind.Rest }),
            RequestedLeaveRestCount = null
        };
        input = input with { DemandMonth = input.DemandMonth with { Employees = [employee] } };

        Assert.AreEqual(SolveStatus.Infeasible, TSolver.Solve(input, new SolverOptions { TimeLimit = TimeSpan.FromSeconds(5) }).Status);
    }

    [TestMethod]
    public void Solve_FixedWorkWithoutElevenHourGap_ReturnsInvalidInput()
    {
        var input = OnlyEmployee(ValidInput(), "T-E1");
        var employee = input.DemandMonth.Employees[0] with
        {
            Assignments = new Dictionary<DateOnly, ScheduleCell>
            {
                [input.DemandMonth.MonthStart] = new() { Kind = AssignmentKind.Work, Shift = Shift.Early }
            }
        };
        input = input with { DemandMonth = input.DemandMonth with { Employees = [employee] } };

        var result = TSolver.Solve(input);

        Assert.AreEqual(SolveStatus.InvalidInput, result.Status);
        Assert.IsTrue(result.Errors.Any(error => error.Field == "Assignments"));
    }

    [TestMethod]
    public void ValidateInput_HistoryAndDemandMayMixShifts()
    {
        static List<InputError> Validate(ScheduleInput value) => (List<InputError>)typeof(TSolver)
            .GetMethod("ValidateInput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [value, new SolverOptions()])!;

        var input = ValidInput();
        var history = input.PreviousMonth.Employees[0];
        var historyDate = input.PreviousMonth.MonthStart;
        history = history with
        {
            Assignments = new Dictionary<DateOnly, ScheduleCell>(history.Assignments)
            {
                [historyDate] = new() { Kind = AssignmentKind.Work, Shift = Shift.Early }
            }
        };
        input = input with
        {
            PreviousMonth = input.PreviousMonth with
            {
                Employees = [history, .. input.PreviousMonth.Employees.Skip(1)]
            }
        };

        var errors = Validate(input);
        Assert.IsFalse(errors.Any(), string.Join(Environment.NewLine, errors));

        var demand = input.DemandMonth.Employees[0];
        demand = demand with
        {
            Assignments = new Dictionary<DateOnly, ScheduleCell>(demand.Assignments)
            {
                [input.DemandMonth.MonthStart.AddDays(2)] = new() { Kind = AssignmentKind.Work, Shift = Shift.Afternoon }
            }
        };
        input = input with
        {
            DemandMonth = input.DemandMonth with
            {
                Employees = [demand, .. input.DemandMonth.Employees.Skip(1)]
            }
        };

        errors = Validate(input);
        Assert.IsFalse(errors.Any(), string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    public void Solve_FixedCrossShiftIsWeightedSoftViolation()
    {
        var input = OnlyEmployee(ValidInput(), "T-E2");
        var employee = input.DemandMonth.Employees[0];
        employee = employee with
        {
            Assignments = new Dictionary<DateOnly, ScheduleCell>(employee.Assignments)
            {
                [input.DemandMonth.MonthStart] = new() { Kind = AssignmentKind.Work, Shift = Shift.Afternoon }
            }
        };
        input = input with { DemandMonth = input.DemandMonth with { Employees = [employee] } };

        var result = TSolver.Solve(input, new SolverOptions { TimeLimit = TimeSpan.FromSeconds(10) });

        Assert.AreNotEqual(SolveStatus.InvalidInput, result.Status, string.Join(Environment.NewLine, result.Errors));
        Assert.AreNotEqual(SolveStatus.Infeasible, result.Status);
        var candidate = result.Candidates[0];
        Assert.AreEqual(Shift.Afternoon, candidate.Schedule.Employees[0].Assignments[input.DemandMonth.MonthStart].Shift);
        var violation = candidate.Objectives.SelectMany(objective => objective.Components).Single(component => component.Name == "NonMonthlyShift");
        Assert.AreEqual(1, violation.Value);
        Assert.AreEqual(9, violation.Weight);
    }

    [TestMethod]
    public void MonthlyRestTargetCountsActualWeekendDays()
    {
        var month = new DateOnly(2026, 8, 1);
        (Type Solver, ScheduleInput Input)[] cases =
        [
            (typeof(MSolver), MSolverTests.ValidInput()),
            (typeof(TSolver), ValidInput())
        ];

        foreach (var (solver, original) in cases)
        {
            var input = original with { DemandMonth = original.DemandMonth with { MonthStart = month } };
            var employee = input.DemandMonth.Employees[0] with { EmploymentStartDate = null };
            var method = solver.GetMethod("ExpectedMonthlyRestCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            Assert.AreEqual(10, method.Invoke(null, [input, employee, false]));
            Assert.AreEqual(4, method.Invoke(null, [input, employee with { EmploymentStartDate = new(2026, 8, 21) }, false]));
        }
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
                    [new(2026, 9, 5)] = new() { Kind = AssignmentKind.LeaveRest, RequestedRest = true }
                },
                RequestedLeaveRestCount = 1
            };
            var schedule = input.DemandMonth with { Employees = [employee] };
            var path = Path.Combine(root, "schedule.csv");

            ScheduleCsv.WriteMonthly(path, schedule);
            var parsed = ScheduleCsv.ReadMonthly(path, schedule.MonthStart);

            Assert.AreEqual(employee.Name, parsed.Employees[0].Name);
            Assert.AreEqual(1, parsed.Employees[0].RequestedLeaveRestCount);
            Assert.IsNull(parsed.Employees[0].Assignments[new(2026, 9, 4)].Kind);
            Assert.AreEqual(AssignmentKind.LeaveRest, parsed.Employees[0].Assignments[new(2026, 9, 5)].Kind);
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
    public void Csv_HistoricalRestAliasesNormalizeToResolvedRequestedRest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var month = new DateOnly(2026, 8, 1);
            var employee = ValidInput().PreviousMonth.Employees[0] with
            {
                Assignments = new Dictionary<DateOnly, ScheduleCell>
                {
                    [month] = new() { Kind = AssignmentKind.Rest, RequestedRest = true },
                    [month.AddDays(1)] = new() { Kind = AssignmentKind.SpecialRest, RequestedRest = true },
                    [month.AddDays(2)] = new() { Kind = AssignmentKind.LeaveRest, RequestedRest = true }
                },
                ClosingUsage = new(1, 1),
                NormalWorkCount = 0
            };
            var inputPath = Path.Combine(root, "input.csv");
            var outputPath = Path.Combine(root, "output.csv");
            ScheduleCsv.WriteMonthly(inputPath, new(month, [employee]));
            File.WriteAllText(inputPath, File.ReadAllText(inputPath)
                .Replace("R*[R]", "R*", StringComparison.Ordinal)
                .Replace("R*[R1]", "R1*", StringComparison.Ordinal)
                .Replace("R*[R休]", "R休*", StringComparison.Ordinal));

            var parsed = ScheduleCsv.ReadMonthly(inputPath, month, historical: true);
            var assignments = parsed.Employees[0].Assignments;
            Assert.AreEqual(AssignmentKind.Rest, assignments[month].Kind);
            Assert.AreEqual(AssignmentKind.SpecialRest, assignments[month.AddDays(1)].Kind);
            Assert.AreEqual(AssignmentKind.LeaveRest, assignments[month.AddDays(2)].Kind);
            Assert.IsTrue(assignments.Values.All(cell => cell.RequestedRest));

            ScheduleCsv.WriteMonthly(outputPath, parsed);
            StringAssert.Contains(File.ReadAllText(outputPath), "R*[R],R*[R1],R*[R休]");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Csv_NonStandardShiftNameAndCodeBecomeEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var shiftsPath = Path.Combine(root, "non-standard-shifts.csv");
            File.WriteAllText(shiftsPath, "班型,時間,代碼\n日一,08:30~17:30,0837\n夜一,22:30~06:30,2235\n");
            var shifts = ScheduleCsv.ReadNonStandardShifts(shiftsPath);
            var schedulePath = Path.Combine(root, "schedule.csv");
            ScheduleCsv.WriteMonthly(schedulePath, ValidInput().DemandMonth);
            var lines = File.ReadAllLines(schedulePath);
            var fields = lines[1].Split(',');
            fields[8] = "日一";
            fields[9] = "0837";
            fields[10] = "2235";
            lines[1] = string.Join(',', fields);
            File.WriteAllLines(schedulePath, lines);

            var parsed = ScheduleCsv.ReadMonthly(schedulePath, new(2026, 9, 1), shifts);
            var assignments = parsed.Employees[0].Assignments;

            Assert.HasCount(2, shifts.Shifts);
            Assert.AreEqual(AssignmentKind.WorkEvent, assignments[new(2026, 9, 1)].Kind);
            Assert.AreEqual(new TimeOnly(8, 30), TimeOnly.FromDateTime(assignments[new(2026, 9, 2)].EventStart!.Value.DateTime));
            Assert.AreEqual(new DateOnly(2026, 9, 4), DateOnly.FromDateTime(assignments[new(2026, 9, 3)].EventEnd!.Value.Date));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Csv_VerifiesMonthlyRestTotals()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var schedule = ValidInput().PreviousMonth;
            var path = Path.Combine(root, "schedule.csv");
            ScheduleCsv.WriteMonthly(path, schedule);
            var parsed = ScheduleCsv.ReadMonthly(path, schedule.MonthStart);

            Assert.IsNull(parsed.Employees[0].RequestedLeaveRestCount);
            Assert.AreEqual(AssignmentKind.LeaveRest, parsed.Employees[0].Assignments[new(2026, 8, 12)].Kind);

            var lines = File.ReadAllLines(path);
            var fields = lines[1].Split(',');
            Assert.AreEqual("4", fields[39]);
            Assert.AreEqual("1", fields[41]);
            fields[41] = "999";
            lines[1] = string.Join(',', fields);
            File.WriteAllLines(path, lines);

            Assert.ThrowsExactly<ScheduleCsvException>(() => ScheduleCsv.ReadMonthly(path, schedule.MonthStart));
            fields[41] = "1";
            fields[39] = "999";
            lines[1] = string.Join(',', fields);
            File.WriteAllLines(path, lines);
            Assert.ThrowsExactly<ScheduleCsvException>(() => ScheduleCsv.ReadMonthly(path, schedule.MonthStart));
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
    public void Cli_Example_RedirectedInputWritesCandidate(string example, bool expectsExternal, bool existingCandidate)
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
            if (existingCandidate) File.WriteAllText(Path.Combine(outputRoot, "candidate-1.csv"), "keep");
            Console.SetIn(new StringReader(string.Join('\n',
                "2026-09",
                Path.Combine(exampleRoot, "previous.csv"),
                Path.Combine(exampleRoot, "demand.csv"),
                Path.Combine(exampleRoot, "rest-intervals.csv"),
                Path.Combine(exampleRoot, "non-standard-shifts.csv")) + "\n"));
            var output = new StringWriter();
            var error = new StringWriter();
            Console.SetOut(output);
            Console.SetError(error);

            Assert.AreEqual(0, Program.Main(), output + Environment.NewLine + error);
            var number = existingCandidate ? 2 : 1;
            Assert.IsTrue(File.Exists(Path.Combine(outputRoot, $"candidate-{number}.csv")));
            Assert.AreEqual(expectsExternal, File.Exists(Path.Combine(outputRoot, $"candidate-{number}-external.csv")));
            if (existingCandidate) Assert.AreEqual("keep", File.ReadAllText(Path.Combine(outputRoot, "candidate-1.csv")));
            Assert.DoesNotContain("覆寫", output.ToString());
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

    [TestMethod]
    public void Solve_UnmarkedLeaveRest_ReturnsInvalidInput()
    {
        var input = ValidInput();
        var employee = input.DemandMonth.Employees[0];
        employee = employee with
        {
            Assignments = new Dictionary<DateOnly, ScheduleCell>(employee.Assignments)
            {
                [new(2026, 9, 3)] = new() { Kind = AssignmentKind.LeaveRest }
            }
        };
        input = input with { DemandMonth = input.DemandMonth with { Employees = [employee, .. input.DemandMonth.Employees.Skip(1)] } };

        Assert.AreEqual(SolveStatus.InvalidInput, TSolver.Solve(input).Status);
    }

    [TestMethod]
    public void Solve_SevenDaysWithoutGeneralRest_LeaveRestDoesNotResetWindow()
    {
        var input = ValidInput();
        var employee = input.DemandMonth.Employees.Single(value => value.EmployeeId == "T-E2");
        employee = employee with
        {
            RequestedLeaveRestCount = 1,
            Assignments = Enumerable.Range(0, 7).ToDictionary(
                offset => input.DemandMonth.MonthStart.AddDays(offset),
                offset => offset == 3
                    ? new ScheduleCell { Kind = AssignmentKind.LeaveRest, RequestedRest = true }
                    : new ScheduleCell { Kind = AssignmentKind.Work, Shift = Shift.Early })
        };
        input = input with
        {
            DemandMonth = input.DemandMonth with
            {
                Employees = input.DemandMonth.Employees.Select(value => value.EmployeeId == employee.EmployeeId ? employee : value).ToArray()
            }
        };

        Assert.AreEqual(SolveStatus.Infeasible, TSolver.Solve(input, new SolverOptions { TimeLimit = TimeSpan.FromSeconds(10) }).Status);
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
                day => id == "T-E1" && day == 11
                    ? new ScheduleCell { Kind = AssignmentKind.LeaveRest }
                    : day % 6 == 5
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
            demand.Add(Row(id, group, ability, currentShift, null, assignments, closing, null, null, id == "T-E2" ? 1 : null));
        }

        demand.Add(Row("T-NEW", "Signal", 4, Shift.Early, new(2026, 9, 21), new Dictionary<DateOnly, ScheduleCell>(), new(12, 1), null, null));
        return new(new(month.AddMonths(-1), previous), new(month, demand), [first, second], new([]));
    }

    private static ScheduleInput OnlyEmployee(ScheduleInput input, string employeeId) => input with
    {
        PreviousMonth = input.PreviousMonth with { Employees = input.PreviousMonth.Employees.Where(employee => employee.EmployeeId == employeeId).ToArray() },
        DemandMonth = input.DemandMonth with { Employees = input.DemandMonth.Employees.Where(employee => employee.EmployeeId == employeeId).ToArray() }
    };

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
        int? workCount,
        int? requestedLeaveRestCount = null) => new()
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
            NormalWorkCount = workCount,
            RequestedLeaveRestCount = requestedLeaveRestCount
        };

}
