using Microsoft.VisualStudio.TestTools.UnitTesting;
using NtmScheduler.Cli;

[assembly: DoNotParallelize]

namespace NtmScheduler.Solvers.Tests;

[TestClass]
public sealed class MSolverTests
{
    private static readonly SolverOptions ShortSolve = new() { TimeLimit = TimeSpan.FromSeconds(3) };

    [TestMethod]
    public void WorkStreakPenaltiesMatchEachModel()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        Assert.AreEqual(0, typeof(MSolver).GetMethod("WorkStreakPenaltyValue", flags)!.Invoke(null, [2]));
        Assert.AreEqual(1, typeof(TSolver).GetMethod("BlockLengthPenaltyValue", flags)!.Invoke(null, [2]));
        Assert.AreEqual(0, typeof(MSolver).GetMethod("WorkStreakPenaltyValue", flags)!.Invoke(null, [5]));
        Assert.AreEqual(1, typeof(TSolver).GetMethod("BlockLengthPenaltyValue", flags)!.Invoke(null, [5]));
    }

    [TestMethod]
    public void CliPortfolioPrefersCompleteThenLexicographicallyBetterMResult()
    {
        var schedule = ValidInput().DemandMonth;
        MSolveResult Result(params ObjectiveScore[] scores) => new(
            SolveStatus.TimeLimit,
            [new MCandidate(schedule, [], scores)],
            []);
        var compare = typeof(Program).GetMethod(
            "CompareMResults",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        int Compare(MSolveResult left, MSolveResult right) => (int)compare.Invoke(null, [left, right])!;

        var incomplete = Result(new ObjectiveScore(1, "RequestedRest", 0, []));
        var complete = Result(
            new ObjectiveScore(1, "RequestedRest", 0, []),
            new ObjectiveScore(4, "ScheduleQualityAndFairness", 200, []));
        var better = Result(
            new ObjectiveScore(1, "RequestedRest", 0, []),
            new ObjectiveScore(4, "ScheduleQualityAndFairness", 100, []));

        Assert.IsLessThan(0, Compare(complete, incomplete));
        Assert.IsLessThan(0, Compare(better, complete));
    }

    [TestMethod]
    public void CliMSearchOptionParsesWorkersSeedsAndSeconds()
    {
        var parse = typeof(Program).GetMethod(
            "ReadMSearchOptions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var options = parse.Invoke(null, [new[] { "--m-search", "workers=3,seeds=4,seconds=90" }])!;
        var type = options.GetType();

        Assert.AreEqual(3, type.GetProperty("Workers")!.GetValue(options));
        Assert.AreEqual(4, type.GetProperty("Seeds")!.GetValue(options));
        Assert.AreEqual(90, type.GetProperty("Seconds")!.GetValue(options));
        Assert.ThrowsExactly<System.Reflection.TargetInvocationException>(() =>
            parse.Invoke(null, [new[] { "--m-search", "workers=3,seeds=0,seconds=90" }]));
    }

    [TestMethod]
    [DataRow(0, 1, 0L)]
    [DataRow(0, 2, 1L)]
    [DataRow(0, 3, 4L)]
    [DataRow(2, 1, 1L)]
    public void SpecialRestBalanceAllowsOneOutstandingDay(int actual, int expected, long penalty)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        Assert.AreEqual(penalty, typeof(MSolver).GetMethod("SpecialRestBalancePenaltyValue", flags)!.Invoke(null, [actual, expected]));
        Assert.AreEqual(penalty, typeof(TSolver).GetMethod("SpecialRestBalancePenaltyValue", flags)!.Invoke(null, [actual, expected]));
    }

    [TestMethod]
    public void Csv_MWorkAliasesNormalizeStationsAndSmallShift()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var month = new DateOnly(2026, 9, 1);
            var employee = ValidInput().DemandMonth.Employees[0] with
            {
                Assignments = new Dictionary<DateOnly, ScheduleCell>
                {
                    [month] = new() { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Afternoon },
                    [month.AddDays(1)] = new() { Kind = AssignmentKind.Work, Station = "LB02", Shift = Shift.Afternoon },
                    [month.AddDays(2)] = new() { Kind = AssignmentKind.Work, Station = "LB03", Shift = Shift.Afternoon },
                    [month.AddDays(3)] = new() { Kind = AssignmentKind.Work, Station = "LB04", Shift = Shift.Afternoon },
                    [month.AddDays(4)] = new() { Kind = AssignmentKind.Work, Station = "LB12", Shift = Shift.Early }
                }
            };
            var inputPath = Path.Combine(root, "input.csv");
            var outputPath = Path.Combine(root, "output.csv");
            ScheduleCsv.WriteMonthly(inputPath, new(month, [employee]));
            File.WriteAllText(inputPath, File.ReadAllText(inputPath)
                .Replace("LB01小", "1小")
                .Replace("LB03小", "3午")
                .Replace("LB04小", "LB04午")
                .Replace("LB12早", "12早"));

            var parsed = ScheduleCsv.ReadMonthly(inputPath, month);
            Assert.AreEqual("LB01", parsed.Employees[0].Assignments[month].Station);
            Assert.IsTrue(parsed.Employees[0].Assignments.Where(pair => pair.Key <= month.AddDays(3)).All(pair => pair.Value.Shift == Shift.Afternoon));
            Assert.AreEqual("LB12", parsed.Employees[0].Assignments[month.AddDays(4)].Station);
            ScheduleCsv.WriteMonthly(outputPath, parsed);
            StringAssert.Contains(File.ReadAllText(outputPath), "LB01小,LB02小,LB03小,LB04小,LB12早");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Csv_MPerpetualScheduleParsesAndRejectsInvalidRows()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "m-perpetual.csv");
            var header = string.Join(',', new[] { "萬年班表" }.Concat(Enumerable.Range(1, 56).Select(value => value.ToString())));
            var cells = Enumerable.Range(0, 56).Select(day => day % 7 is 0 or 4 ? "R" : day % 2 == 0 ? "1早" : "LB01小").ToArray();
            cells[2] = "";
            var row = string.Join(',', new[] { "LB01-1" }.Concat(cells));
            File.WriteAllText(path, header + Environment.NewLine + row + Environment.NewLine);

            var schedule = ScheduleCsv.ReadMPerpetualSchedule(path);

            Assert.HasCount(56, schedule.Patterns["LB01-1"]);
            Assert.AreEqual(AssignmentKind.Rest, schedule.Patterns["LB01-1"][0]!.Kind);
            Assert.AreEqual(("LB01", Shift.Afternoon), (schedule.Patterns["LB01-1"][1]!.Station, schedule.Patterns["LB01-1"][1]!.Shift));
            Assert.IsNull(schedule.Patterns["LB01-1"][2]);

            File.AppendAllText(path, row + Environment.NewLine);
            Assert.ThrowsExactly<ScheduleCsvException>(() => ScheduleCsv.ReadMPerpetualSchedule(path));
            cells[0] = "R1";
            File.WriteAllText(path, header + Environment.NewLine + string.Join(',', new[] { "LB01-1" }.Concat(cells)) + Environment.NewLine);
            Assert.ThrowsExactly<ScheduleCsvException>(() => ScheduleCsv.ReadMPerpetualSchedule(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Csv_MonthlyPerpetualScheduleIdRoundTripsAndLegacyRemainsReadable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ntm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var schedule = ValidInput().DemandMonth;
            var employee = schedule.Employees[0] with { PerpetualScheduleId = "LB01-1" };
            schedule = schedule with { Employees = [employee] };
            var path = Path.Combine(root, "schedule.csv");
            ScheduleCsv.WriteMonthly(path, schedule);
            Assert.AreEqual("LB01-1", ScheduleCsv.ReadMonthly(path, schedule.MonthStart).Employees[0].PerpetualScheduleId);

            var legacy = File.ReadAllLines(path)
                .Select(line => string.Join(',', line.Split(',').SkipLast(1)))
                .ToArray();
            File.WriteAllLines(path, legacy);
            Assert.IsNull(ScheduleCsv.ReadMonthly(path, schedule.MonthStart).Employees[0].PerpetualScheduleId);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow("2026-07-20", 0)]
    [DataRow("2026-09-13", 55)]
    [DataRow("2026-09-14", 0)]
    public void PerpetualScheduleDayIndexStartsAtEachRestInterval(string dateText, int expected)
    {
        var method = typeof(MSolver).GetMethod("PerpetualScheduleDayIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.AreEqual(expected, method.Invoke(null, [ValidInput(), DateOnly.Parse(dateText)]));
    }

    [TestMethod]
    public void Solve_PerpetualScheduleIdInheritsAndConflictingHintRemainsNonBinding()
    {
        var input = ValidInput();
        var employeeId = input.DemandMonth.Employees[0].EmployeeId;
        input = input with
        {
            PreviousMonth = input.PreviousMonth with
            {
                Employees = input.PreviousMonth.Employees.Select(employee => employee.EmployeeId == employeeId
                    ? employee with { PerpetualScheduleId = "LB01-1" }
                    : employee).ToArray()
            }
        };
        ScheduleCell?[] pattern = Enumerable.Range(0, 56)
            .Select(_ => new ScheduleCell { Kind = AssignmentKind.Rest })
            .ToArray();
        pattern[0] = null;

        var result = MSolver.Solve(input, new MPerpetualSchedule(new Dictionary<string, IReadOnlyList<ScheduleCell?>>
        {
            ["LB01-1"] = pattern
        }), ShortSolve);

        Assert.AreNotEqual(SolveStatus.InvalidInput, result.Status, string.Join(Environment.NewLine, result.Errors));
        Assert.AreNotEqual(SolveStatus.Infeasible, result.Status);
        Assert.IsGreaterThanOrEqualTo(1, result.Candidates.Count);
        Assert.AreEqual("LB01-1", result.Candidates[0].Schedule.Employees.Single(employee => employee.EmployeeId == employeeId).PerpetualScheduleId);
    }

    [TestMethod]
    public void Solve_PerpetualScheduleRejectsUnknownAndCrossGroupPatterns()
    {
        var input = ValidInput();
        var employee = input.DemandMonth.Employees[0] with { PerpetualScheduleId = "LB01-1" };
        input = input with { DemandMonth = input.DemandMonth with { Employees = [employee, .. input.DemandMonth.Employees.Skip(1)] } };

        var unknown = MSolver.Solve(input, new MPerpetualSchedule(new Dictionary<string, IReadOnlyList<ScheduleCell?>>()), ShortSolve);
        Assert.AreEqual(SolveStatus.InvalidInput, unknown.Status);
        Assert.IsTrue(unknown.Errors.Any(error => error.Field == "DemandMonth.PerpetualScheduleId"));

        var crossGroup = Enumerable.Range(0, 56)
            .Select(_ => new ScheduleCell { Kind = AssignmentKind.Work, Station = "LB04", Shift = Shift.Early })
            .ToArray();
        var invalid = MSolver.Solve(input, new MPerpetualSchedule(new Dictionary<string, IReadOnlyList<ScheduleCell?>>
        {
            ["LB01-1"] = crossGroup
        }), ShortSolve);
        Assert.AreEqual(SolveStatus.InvalidInput, invalid.Status);
        Assert.IsTrue(invalid.Errors.Any(error => error.Field == "DemandMonth.PerpetualScheduleId"));
    }

    [TestMethod]
    public void Solve_Lb09ExternalAllowanceUsesFourAssignmentThreshold()
    {
        var input = ValidInput();
        var date = input.DemandMonth.MonthStart;
        var group = input.DemandMonth.Employees.Where(employee => employee.Affiliation == "LB07").ToArray();
        var fixedWork = new (string Station, Shift Shift)[]
        {
            ("LB07", Shift.Early), ("LB07", Shift.Afternoon), ("LB08", Shift.Early),
            ("LB08", Shift.Afternoon), ("LB08", Shift.Night), ("LB07", Shift.Early),
            ("LB07", Shift.Early), ("LB07", Shift.Early), ("LB07", Shift.Early), ("LB07", Shift.Early)
        };
        input = input with
        {
            DemandMonth = input.DemandMonth with
            {
                Employees = input.DemandMonth.Employees.Select(employee =>
                {
                    var index = Array.IndexOf(group, employee);
                    return index < 0 ? employee : employee with
                    {
                        Assignments = new Dictionary<DateOnly, ScheduleCell>(employee.Assignments)
                        {
                            [date] = new() { Kind = AssignmentKind.Work, Station = fixedWork[index].Station, Shift = fixedWork[index].Shift }
                        }
                    };
                }).ToArray()
            }
        };

        var result = MSolver.Solve(input, new SolverOptions { TimeLimit = TimeSpan.FromSeconds(10) });

        Assert.IsGreaterThanOrEqualTo(1, result.Candidates.Count);
        var candidate = result.Candidates[0];
        Assert.AreEqual(2, candidate.ExternalAssignments.Where(item => item.Date == date && item.Station == "LB09").Sum(item => item.Count));
        var legacyTotal = candidate.ExternalAssignments.Where(item => item.Station != "LB09").Sum(item => item.Count);
        var lb09Total = candidate.ExternalAssignments.Where(item => item.Station == "LB09").Sum(item => item.Count);
        Assert.AreEqual(
            Math.Max(0, legacyTotal - 70) + Math.Max(0, lb09Total - 4),
            candidate.Objectives.SelectMany(objective => objective.Components).Single(component => component.Name == "ExternalStaffing").Value);
    }

    [TestMethod]
    public void Solve_MonthlySchedules_ReturnsNamedCandidate()
    {
        var input = ValidInput();
        var result = MSolver.Solve(input, new SolverOptions { TimeLimit = TimeSpan.FromSeconds(10) });

        Assert.AreNotEqual(SolveStatus.InvalidInput, result.Status, string.Join(Environment.NewLine, result.Errors));
        Assert.AreNotEqual(SolveStatus.Infeasible, result.Status);
        Assert.AreEqual(SolveStatus.TimeLimit, result.Status);
        Assert.IsGreaterThanOrEqualTo(1, result.Candidates.Count);
        var comparable = (from employee in input.DemandMonth.Employees
                          from date in Enumerable.Range(0, 30).Select(input.DemandMonth.MonthStart.AddDays)
                          where (employee.EmploymentStartDate is not { } start || date >= start) &&
                                employee.Assignments.GetValueOrDefault(date)?.Kind is null
                          select (employee.EmployeeId, Date: date)).ToArray();
        var baselineScores = result.Candidates[0].Objectives.ToDictionary(objective => objective.Name, objective => objective.Value);
        foreach (var alternative in result.Candidates.Skip(1))
        {
            var first = result.Candidates[0].Schedule.Employees.ToDictionary(employee => employee.EmployeeId);
            var second = alternative.Schedule.Employees.ToDictionary(employee => employee.EmployeeId);
            Assert.IsGreaterThanOrEqualTo(
                (int)Math.Ceiling(comparable.Length * 0.05),
                comparable.Count(cell => first[cell.EmployeeId].Assignments[cell.Date] != second[cell.EmployeeId].Assignments[cell.Date]));
            foreach (var objective in alternative.Objectives)
                Assert.IsLessThanOrEqualTo(
                    baselineScores[objective.Name] * 6,
                    objective.Value * 5,
                    $"{objective.Name} exceeds the 20% M candidate quality allowance.");
        }
        var candidate = result.Candidates[0];
        CollectionAssert.AreEqual(
            new[] { "RequestedRest", "ScheduleQualityAndFairness" },
            candidate.Objectives.Select(value => value.Name).ToArray());
        Assert.AreEqual(new DateOnly(2026, 9, 1), candidate.Schedule.MonthStart);
        Assert.HasCount(40, candidate.Schedule.Employees);
        Assert.IsNull(candidate.Schedule.Employees[0].EmploymentStartDate);
        var leaveEmployee = candidate.Schedule.Employees.Single(employee => employee.EmployeeId == "M1-01");
        Assert.AreEqual(1, leaveEmployee.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.LeaveRest && cell.RequestedRest));
        Assert.IsNull(leaveEmployee.RequestedLeaveRestCount);
        Assert.IsGreaterThanOrEqualTo(1, candidate.Objectives
            .SelectMany(value => value.Components)
            .Single(value => value.Name == "NightRestEarly").Value);

        long ExpectedDeviationTenths(Shift shift) => candidate.Schedule.Employees
            .GroupBy(employee => (int.Parse(employee.Affiliation[2..]) - 1) / 3)
            .Sum(group =>
            {
                var counts = group.Select(employee => (long)employee.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.Work && cell.Shift == shift)).ToArray();
                var total = counts.Sum();
                return 10 * counts.Sum(value => Math.Abs(counts.Length * value - total)) / counts.Length;
            });
        var fairness = candidate.Objectives.Single(objective => objective.Name == "ScheduleQualityAndFairness").Components;
        Assert.AreEqual((ExpectedDeviationTenths(Shift.Early), 2), (fairness.Single(component => component.Name == "EarlyShiftFairness").Value, fairness.Single(component => component.Name == "EarlyShiftFairness").Weight));
        Assert.AreEqual((ExpectedDeviationTenths(Shift.Afternoon), 2), (fairness.Single(component => component.Name == "AfternoonShiftFairness").Value, fairness.Single(component => component.Name == "AfternoonShiftFairness").Weight));
        Assert.AreEqual((ExpectedDeviationTenths(Shift.Night), 10), (fairness.Single(component => component.Name == "NightShiftFairness").Value, fairness.Single(component => component.Name == "NightShiftFairness").Weight));
        SolverAcceptanceAssertions.AssertMHardRules(input, candidate);
        SolverAcceptanceAssertions.AssertMSoftRules(input, candidate);

    }

    [TestMethod]
    public void Solve_OldEmployeeWithoutHistory_ReturnsInvalidInput()
    {
        var input = ValidInput();
        input = input with { PreviousMonth = input.PreviousMonth with { Employees = input.PreviousMonth.Employees.Skip(1).ToArray() } };

        var result = MSolver.Solve(input, ShortSolve);

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

        var result = MSolver.Solve(input, ShortSolve);

        Assert.AreEqual(SolveStatus.InvalidInput, result.Status);
        Assert.IsGreaterThanOrEqualTo(2, result.Errors.Count(error => error.Field == nameof(ScheduleInput.RestIntervals)));
    }

    [TestMethod]
    public void Solve_PreCanceledToken_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => MSolver.Solve(ValidInput(), ShortSolve, cancellation.Token));
    }

    [TestMethod]
    public void Solve_ExpiredBudget_ReturnsTimeLimit()
    {
        var result = MSolver.Solve(ValidInput(), new SolverOptions { TimeLimit = TimeSpan.FromTicks(1) });
        Assert.AreEqual(SolveStatus.TimeLimit, result.Status);
    }

    [TestMethod]
    public void Solve_NegativeRequestedLeaveRestLimit_ReturnsInvalidInput()
    {
        var input = ValidInput();
        var employee = input.DemandMonth.Employees[0] with { RequestedLeaveRestCount = -1 };
        input = input with { DemandMonth = input.DemandMonth with { Employees = [employee, .. input.DemandMonth.Employees.Skip(1)] } };

        var result = MSolver.Solve(input, ShortSolve);

        Assert.AreEqual(SolveStatus.InvalidInput, result.Status);
        Assert.IsTrue(result.Errors.Any(error => error.Field == "DemandMonth.RequestedLeaveRestCount"));
    }

    [TestMethod]
    public void Solve_FixedLeaveRestAboveRequestedCount_ReturnsInvalidInput()
    {
        var input = ValidInput();
        var employee = input.DemandMonth.Employees[0];
        var requestedDate = employee.Assignments.Single(pair => pair.Value.RequestedRest).Key;
        employee = employee with
        {
            RequestedLeaveRestCount = 0,
            Assignments = new Dictionary<DateOnly, ScheduleCell>(employee.Assignments)
            {
                [requestedDate] = new() { Kind = AssignmentKind.LeaveRest, RequestedRest = true }
            }
        };
        input = input with { DemandMonth = input.DemandMonth with { Employees = [employee, .. input.DemandMonth.Employees.Skip(1)] } };

        Assert.AreEqual(SolveStatus.InvalidInput, MSolver.Solve(input, ShortSolve).Status);
    }

    [TestMethod]
    public void Solve_TwoEmployeesInSameRequiredPosition_IsFeasible()
    {
        var input = ValidInput();
        var date = input.DemandMonth.MonthStart;
        var employees = input.DemandMonth.Employees.Select(employee =>
            employee.EmployeeId == "M1-03"
                ? employee with
                {
                    Assignments = new Dictionary<DateOnly, ScheduleCell>(employee.Assignments)
                    {
                        [date] = new() { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Early }
                    }
                }
                : employee).ToArray();
        input = input with { DemandMonth = input.DemandMonth with { Employees = employees } };

        var result = MSolver.Solve(input, ShortSolve);

        Assert.IsGreaterThanOrEqualTo(1, result.Candidates.Count);
        Assert.IsGreaterThanOrEqualTo(2, result.Candidates[0].Schedule.Employees.Count(employee =>
            employee.Assignments[date].Kind == AssignmentKind.Work &&
            employee.Assignments[date].Station == "LB01" &&
            employee.Assignments[date].Shift == Shift.Early));
    }

    [TestMethod]
    public void Solve_TwoEmployeesInSameNightPosition_IsInfeasible()
    {
        var input = ValidInput();
        var date = input.DemandMonth.MonthStart;
        var fixedEmployees = new HashSet<string> { "M1-04", "M1-06" };
        var employees = input.DemandMonth.Employees.Select(employee =>
            fixedEmployees.Contains(employee.EmployeeId)
                ? employee with
                {
                    Assignments = new Dictionary<DateOnly, ScheduleCell>(employee.Assignments)
                    {
                        [date] = new() { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Night }
                    }
                }
                : employee).ToArray();
        input = input with { DemandMonth = input.DemandMonth with { Employees = employees } };

        Assert.AreEqual(SolveStatus.Infeasible, MSolver.Solve(input, ShortSolve).Status);
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
                var requestedLeaveRest = group == 0 && index == 0 && day == 0;
                targetAssignments[date] = new()
                {
                    Kind = requestedLeaveRest ? null : AssignmentKind.Rest,
                    RequestedRest = requestedLeaveRest
                };
            }
            if (group == 0 && index == 0) targetAssignments[month.AddDays(1)] = Event(month.AddDays(1));
            if (group == 0 && index == 1) targetAssignments[month] = new() { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Early };
            if (group == 0 && index == 2) targetAssignments[month] = new() { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Afternoon };

            var firstIntervalRests = targetAssignments.Count(pair => pair.Key <= firstInterval.End && pair.Value.Kind == AssignmentKind.Rest);
            var closing = new RestUsage(16 - firstIntervalRests, 1);
            var historyAssignments = Enumerable.Range(0, 31).ToDictionary(
                day => new DateOnly(2026, 8, 1).AddDays(day),
                day => IsRestDay(day, index)
                    ? new ScheduleCell { Kind = AssignmentKind.Rest }
                    : new ScheduleCell { Kind = AssignmentKind.Work, Station = homes[group], Shift = Shift.Early });
            if (group == 0 && index is 1 or 2)
            {
                historyAssignments[new(2026, 8, 30)] = new() { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Night };
                historyAssignments[new(2026, 8, 31)] = new() { Kind = AssignmentKind.Rest };
            }

            previous.Add(Row(id, $"M Employee {id}", homes[group], null, historyAssignments, null, closing, historyAssignments.Count(pair => pair.Value.Kind == AssignmentKind.Work)));
            demand.Add(Row(id, $"M Employee {id}", homes[group], null, targetAssignments, closing, null, null, group == 0 && index == 0 ? 1 : null));
        }

        return new(
            new MonthlySchedule(month.AddMonths(-1), previous),
            new MonthlySchedule(month, demand),
            [firstInterval, secondInterval],
            new([]));
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
        int? workCount,
        int? requestedLeaveRestCount = null) => new()
        {
            EmployeeId = id,
            Name = name,
            Affiliation = affiliation,
            EmploymentStartDate = start,
            Assignments = assignments,
            OpeningUsage = opening,
            ClosingUsage = closing,
            NormalWorkCount = workCount,
            RequestedLeaveRestCount = requestedLeaveRestCount
        };
}
