using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;

namespace NtmScheduler.Solvers;

/// <summary>Builds and solves the station-service schedule from two typed monthly schedules.</summary>
public static partial class MSolver
{
    // Fixed M configuration

    private const int ExtensionDays = 7;

    private static readonly string[] Stations =
    [
        "LB01", "LB02", "LB03", "LB04", "LB05", "LB06",
        "LB07", "LB08", "LB09", "LB10", "LB11", "LB12"
    ];

    private static readonly Shift[] Shifts = [Shift.Early, Shift.Afternoon, Shift.Night];
    private static readonly HashSet<string> ExternalStations = ["LB02", "LB04", "LB11"];
    private static readonly HashSet<string> NightStations = ["LB01", "LB06", "LB08", "LB12"];

    /// <summary>Validates input, optimizes each named objective in priority order, and returns up to three candidates.</summary>
    public static MSolveResult Solve(
        ScheduleInput input,
        SolverOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new SolverOptions();
        if (input is null) return new(SolveStatus.InvalidInput, [], [new(nameof(input), "ScheduleInput is required.")]);

        var missing = FindMissingCollections(input);
        if (missing.Count > 0) return new(SolveStatus.InvalidInput, [], missing);
        input = CopyInput(input);
        var errors = ValidateInput(input, options);
        if (errors.Count > 0) return new(SolveStatus.InvalidInput, [], errors);

        var targetDates = TargetMonthDates(input).ToArray();
        var modelDates = PlanningHorizonDates(input).ToArray();
        var model = new CpModel();

        var variables = CreateDecisionVariables(model, input, modelDates);
        AddHardConstraints(model, input, modelDates, variables);

        var solver = new CpSolver();
        using var registration = cancellationToken.Register(solver.StopSearch);
        var stopwatch = Stopwatch.StartNew();
        var candidates = new List<MCandidate>(3);
        MCandidate? current = null;

        if (!ConfigureRemainingSearchTime(solver, options, stopwatch))
            return new(SolveStatus.TimeLimit, candidates, []);
        var feasibilityStatus = solver.Solve(model);
        cancellationToken.ThrowIfCancellationRequested();
        if (feasibilityStatus == CpSolverStatus.Infeasible) return new(SolveStatus.Infeasible, [], []);
        if (feasibilityStatus == CpSolverStatus.ModelInvalid) throw new InvalidOperationException("The M CP-SAT model is invalid.");
        if (feasibilityStatus is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            return new(SolveStatus.TimeLimit, candidates, []);
        current = ReadCandidate(solver, input, targetDates, variables, []);
        AddSolutionHints(model, solver, variables);
        var objectives = BuildObjectiveGroups(model, input, targetDates, modelDates, variables);

        foreach (var objective in objectives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ConfigureRemainingSearchTime(solver, options, stopwatch))
            {
                if (current is not null) candidates.Add(current);
                return new(SolveStatus.TimeLimit, candidates, []);
            }

            model.Minimize(objective.Total);
            var status = solver.Solve(model);
            cancellationToken.ThrowIfCancellationRequested();
            if (status == CpSolverStatus.Infeasible) return new(SolveStatus.Infeasible, [], []);
            if (status == CpSolverStatus.ModelInvalid) throw new InvalidOperationException("The M CP-SAT model is invalid.");
            if (status != CpSolverStatus.Optimal)
            {
                if (status == CpSolverStatus.Feasible) current = ReadCandidate(solver, input, targetDates, variables, objectives);
                if (current is not null) candidates.Add(current);
                return new(SolveStatus.TimeLimit, candidates, []);
            }

            current = ReadCandidate(solver, input, targetDates, variables, objectives);
            model.Add(objective.Total == solver.Value(objective.Total));
        }

        candidates.Add(current!);
        SearchAdditionalCandidates(model, solver, input, options, stopwatch, targetDates, variables, objectives, candidates, cancellationToken);
        return new(SolveStatus.Optimal, candidates, []);
    }

    private static void AddSolutionHints(CpModel model, CpSolver solver, ModelVariables variables)
    {
        foreach (var variable in variables.Work.Values
                     .Concat(variables.WorksShift.Values)
                     .Concat(variables.Rest.Values)
                     .Concat(variables.SpecialRest.Values)
                     .Concat(variables.LeaveRest.Values)
                     .Concat(variables.AnyRest.Values)
                     .Concat(variables.ActualWork.Values)
                     .Concat(variables.SupportsOtherStation.Values))
            model.AddHint(variable, solver.Value(variable));
        foreach (var variable in variables.External.Values)
            model.AddHint(variable, solver.Value(variable));
    }

    // Candidate generation

    private static void SearchAdditionalCandidates(
        CpModel model,
        CpSolver solver,
        ScheduleInput input,
        SolverOptions options,
        Stopwatch stopwatch,
        IReadOnlyList<DateOnly> targetDates,
        ModelVariables variables,
        IReadOnlyList<ObjectiveGroup> objectives,
        List<MCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var comparable = (from employee in input.DemandMonth.Employees
                          from date in targetDates
                          where IsEmployedOn(employee, date) && employee.Assignments.GetValueOrDefault(date)?.Kind is null
                          select (Employee: employee.EmployeeId, Date: date))
            .ToArray();
        if (comparable.Length == 0) return;
        var minimumDifference = (int)Math.Ceiling(comparable.Length * 0.10);
        var priorChoices = new List<BoolVar[]> { SelectedAssignmentVariables(solver, comparable, variables) };

        while (candidates.Count < 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            model.Add(LinearExpr.Sum(priorChoices[^1].Select(variable => 1 - variable)) >= minimumDifference);
            if (!ConfigureRemainingSearchTime(solver, options, stopwatch)) return;
            var status = solver.Solve(model);
            cancellationToken.ThrowIfCancellationRequested();
            if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible)) return;
            candidates.Add(ReadCandidate(solver, input, targetDates, variables, objectives));
            priorChoices.Add(SelectedAssignmentVariables(solver, comparable, variables));
        }
    }

    private static BoolVar[] SelectedAssignmentVariables(
        CpSolver solver,
        IReadOnlyList<(string Employee, DateOnly Date)> cells,
        ModelVariables variables)
    {
        var selected = new List<BoolVar>(cells.Count);
        foreach (var cell in cells)
        {
            if (solver.Value(variables.Rest[cell]) == 1) selected.Add(variables.Rest[cell]);
            else if (solver.Value(variables.SpecialRest[cell]) == 1) selected.Add(variables.SpecialRest[cell]);
            else if (solver.Value(variables.LeaveRest[cell]) == 1) selected.Add(variables.LeaveRest[cell]);
            else selected.Add(variables.Work.Single(x => x.Key.Employee == cell.Employee && x.Key.Date == cell.Date && solver.Value(x.Value) == 1).Value);
        }
        return selected.ToArray();
    }

    // Result mapping

    private static MCandidate ReadCandidate(
        CpSolver solver,
        ScheduleInput input,
        IReadOnlyList<DateOnly> targetDates,
        ModelVariables variables,
        IReadOnlyList<ObjectiveGroup> objectives)
    {
        var monthEnd = targetDates[^1];
        var closingInterval = RestIntervalContaining(input, monthEnd);
        var employees = new List<EmployeeMonthlySchedule>(input.DemandMonth.Employees.Count);

        foreach (var employee in input.DemandMonth.Employees)
        {
            var assignments = new Dictionary<DateOnly, ScheduleCell>();
            foreach (var date in targetDates.Where(date => IsEmployedOn(employee, date)))
            {
                var source = employee.Assignments.GetValueOrDefault(date);
                var requested = source?.RequestedRest == true;
                if (source?.Kind == AssignmentKind.WorkEvent)
                {
                    assignments[date] = source;
                }
                else if (solver.Value(variables.Rest[(employee.EmployeeId, date)]) == 1)
                {
                    assignments[date] = new() { Kind = AssignmentKind.Rest, RequestedRest = requested };
                }
                else if (solver.Value(variables.SpecialRest[(employee.EmployeeId, date)]) == 1)
                {
                    assignments[date] = new() { Kind = AssignmentKind.SpecialRest, RequestedRest = requested };
                }
                else if (solver.Value(variables.LeaveRest[(employee.EmployeeId, date)]) == 1)
                {
                    assignments[date] = new() { Kind = AssignmentKind.LeaveRest, RequestedRest = requested };
                }
                else
                {
                    var work = variables.Work.Single(x => x.Key.Employee == employee.EmployeeId && x.Key.Date == date && solver.Value(x.Value) == 1);
                    assignments[date] = new() { Kind = AssignmentKind.Work, Station = work.Key.Station, Shift = work.Key.Shift, RequestedRest = requested };
                }
            }

            var closingDates = targetDates.Where(date => date >= closingInterval.Start && date <= closingInterval.End && IsEmployedOn(employee, date));
            var prior = RestUsageBeforeModeledDates(input, employee, closingInterval);
            var closing = new RestUsage(
                prior.Rest + closingDates.Sum(date => (int)solver.Value(variables.Rest[(employee.EmployeeId, date)])),
                prior.SpecialRest + closingDates.Sum(date => (int)solver.Value(variables.SpecialRest[(employee.EmployeeId, date)])));
            var workCount = targetDates.Where(date => IsEmployedOn(employee, date))
                .Sum(date => variables.Work.Where(x => x.Key.Employee == employee.EmployeeId && x.Key.Date == date).Sum(x => (int)solver.Value(x.Value)));

            employees.Add(employee with
            {
                Assignments = assignments,
                OpeningUsage = OpeningRestUsage(input, employee),
                ClosingUsage = closing,
                NormalWorkCount = workCount,
                RequestedLeaveRestCount = null
            });
        }

        var external = variables.External
            .Where(x => targetDates.Contains(x.Key.Date) && solver.Value(x.Value) > 0)
            .Select(x => new MExternalAssignment(x.Key.Date, x.Key.Station, x.Key.Shift, (int)solver.Value(x.Value)))
            .OrderBy(x => x.Date).ThenBy(x => x.Station).ThenBy(x => x.Shift)
            .ToArray();
        var scores = objectives.Select(objective => new ObjectiveScore(
            objective.Priority,
            objective.Name,
            solver.Value(objective.Total),
            objective.Components.Select(component => new ObjectiveComponent(component.Name, solver.Value(component.Expression), component.Weight)).ToArray())).ToArray();

        return new(new MonthlySchedule(input.DemandMonth.MonthStart, employees), external, scores);
    }

    // Applies the remaining global time budget to the next CP-SAT search.
    private static bool ConfigureRemainingSearchTime(CpSolver solver, SolverOptions options, Stopwatch stopwatch)
    {
        var remaining = options.TimeLimit - stopwatch.Elapsed;
        if (remaining <= TimeSpan.Zero) return false;
        solver.StringParameters = string.Create(
            CultureInfo.InvariantCulture,
            $"max_time_in_seconds:{remaining.TotalSeconds} random_seed:{options.RandomSeed} num_search_workers:{options.WorkerCount}");
        return true;
    }

}
