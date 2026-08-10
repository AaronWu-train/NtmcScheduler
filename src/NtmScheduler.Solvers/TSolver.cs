using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;

namespace NtmScheduler.Solvers;

/// <summary>Builds and solves the inspection-team schedule from two typed monthly schedules.</summary>
public static partial class TSolver
{
    // Fixed T configuration

    private const int ExtensionDays = 7;
    private static readonly Shift[] Shifts = [Shift.Early, Shift.Afternoon, Shift.Night];

    /// <summary>Validates input, optimizes each named objective in priority order, and returns up to three candidates.</summary>
    public static TSolveResult Solve(
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
        var objectives = BuildObjectiveGroups(model, input, targetDates, modelDates, variables);

        var solver = new CpSolver();
        using var registration = cancellationToken.Register(solver.StopSearch);
        var stopwatch = Stopwatch.StartNew();
        var candidates = new List<TCandidate>(3);
        TCandidate? current = null;

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
            if (status == CpSolverStatus.ModelInvalid) throw new InvalidOperationException("The T CP-SAT model is invalid.");
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
        List<TCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var comparable = (from employee in input.DemandMonth.Employees
                          from date in targetDates
                          where IsEmployedOn(employee, date) && employee.Assignments.GetValueOrDefault(date)?.Kind is null
                          select (Employee: employee.EmployeeId, Date: date))
            .ToArray();
        if (comparable.Length == 0) return;
        var minimumDifference = (int)Math.Ceiling(comparable.Length * 0.10);
        var priorChoices = new List<BoolVar[]> { SelectedAssignmentVariables(solver, input, comparable, variables) };
        while (candidates.Count < 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            model.Add(LinearExpr.Sum(priorChoices[^1].Select(variable => 1 - variable)) >= minimumDifference);
            if (!ConfigureRemainingSearchTime(solver, options, stopwatch)) return;
            var status = solver.Solve(model);
            cancellationToken.ThrowIfCancellationRequested();
            if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible)) return;
            candidates.Add(ReadCandidate(solver, input, targetDates, variables, objectives));
            priorChoices.Add(SelectedAssignmentVariables(solver, input, comparable, variables));
        }
    }

    private static BoolVar[] SelectedAssignmentVariables(
        CpSolver solver,
        ScheduleInput input,
        IReadOnlyList<(string Employee, DateOnly Date)> cells,
        ModelVariables variables)
    {
        var selected = new List<BoolVar>(cells.Count);
        foreach (var cell in cells)
        {
            if (solver.Value(variables.Rest[cell]) == 1) selected.Add(variables.Rest[cell]);
            else if (solver.Value(variables.SpecialRest[cell]) == 1) selected.Add(variables.SpecialRest[cell]);
            else if (solver.Value(variables.LeaveRest[cell]) == 1) selected.Add(variables.LeaveRest[cell]);
            else
            {
                var shift = SelectedWorkShift(solver, variables, cell.Employee, cell.Date);
                selected.Add(variables.Work[(cell.Employee, cell.Date, shift)]);
            }
        }
        return selected.ToArray();
    }

    // Result mapping

    private static TCandidate ReadCandidate(
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
                if (source?.Kind == AssignmentKind.WorkEvent) assignments[date] = source;
                else if (solver.Value(variables.Rest[(employee.EmployeeId, date)]) == 1) assignments[date] = new() { Kind = AssignmentKind.Rest, RequestedRest = requested };
                else if (solver.Value(variables.SpecialRest[(employee.EmployeeId, date)]) == 1) assignments[date] = new() { Kind = AssignmentKind.SpecialRest, RequestedRest = requested };
                else if (solver.Value(variables.LeaveRest[(employee.EmployeeId, date)]) == 1) assignments[date] = new() { Kind = AssignmentKind.LeaveRest, RequestedRest = requested };
                else assignments[date] = new() { Kind = AssignmentKind.Work, Shift = SelectedWorkShift(solver, variables, employee.EmployeeId, date), RequestedRest = requested };
            }

            var closingDates = targetDates.Where(date => date >= closingInterval.Start && date <= closingInterval.End && IsEmployedOn(employee, date));
            var prior = RestUsageBeforeModeledDates(input, employee, closingInterval);
            var closing = new RestUsage(
                prior.Rest + closingDates.Sum(date => (int)solver.Value(variables.Rest[(employee.EmployeeId, date)])),
                prior.SpecialRest + closingDates.Sum(date => (int)solver.Value(variables.SpecialRest[(employee.EmployeeId, date)])));
            var workCount = assignments.Values.Count(cell => cell.Kind == AssignmentKind.Work);

            employees.Add(employee with
            {
                Assignments = assignments,
                OpeningUsage = OpeningRestUsage(input, employee),
                ClosingUsage = closing,
                NormalWorkCount = workCount,
                RequestedLeaveRestCount = null
            });
        }

        var scores = objectives.Select(objective => new ObjectiveScore(
            objective.Priority,
            objective.Name,
            solver.Value(objective.Total),
            objective.Components.Select(component => new ObjectiveComponent(component.Name, solver.Value(component.Expression), component.Weight)).ToArray())).ToArray();
        return new(new MonthlySchedule(input.DemandMonth.MonthStart, employees), scores);
    }

    private static Shift SelectedWorkShift(CpSolver solver, ModelVariables variables, string employee, DateOnly date) =>
        Shifts.Single(shift => solver.Value(variables.Work[(employee, date, shift)]) == 1);

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
