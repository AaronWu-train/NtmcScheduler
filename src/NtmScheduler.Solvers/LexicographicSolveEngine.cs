using Google.OrTools.Sat;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Solvers.Common;
using NtmScheduler.Solvers.M;
using NtmScheduler.Solvers.T;

namespace NtmScheduler.Solvers;

public sealed class LexicographicSolveEngine
{
    private readonly RuleEvaluationEngine _evaluator = new();

    public SolveResult Solve(
        SolveRequest request,
        IProgress<SolveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var budget = new SolveBudget(request.TotalTimeLimit);
        var built = request.Unit == Unit.M
            ? new MModelBuilder(request).Build()
            : new TModelBuilder(request).Build();

        var model = built.Model;
        var rules = request.SoftRules.Where(r => r.Enabled).OrderBy(r => r.Order).ToList();
        var completed = new List<string>();
        var optStatus = OptimizationStatus.Optimal;

        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>>? incumbentAssign = null;
        IReadOnlySet<(string Station, DateOnly Date, ShiftType Shift)>? incumbentExt = null;
        Dictionary<string, int>? incumbentMetrics = null;

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (budget.Exhausted && incumbentAssign is not null)
            {
                optStatus = OptimizationStatus.TimeLimit;
                break;
            }

            if (!built.SoftObjectives.TryGetValue(rule.RuleId, out var obj))
                continue;

            progress?.Report(new SolveProgress(rule.RuleId, completed, null, $"最佳化 {rule.RuleId}"));
            model.Minimize(obj);
            var solver = CreateSolver(request, budget);
            var status = solver.Solve(model);

            if (status is CpSolverStatus.Infeasible or CpSolverStatus.ModelInvalid)
            {
                if (incumbentAssign is null && completed.Count == 0)
                    return CreateInfeasible(request, budget);
                throw new InvalidOperationException($"P0 未變卻於 {rule.RuleId} 無解");
            }

            if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            {
                if (incumbentAssign is null)
                    return CreateInfeasible(request, budget);
                optStatus = OptimizationStatus.TimeLimit;
                break;
            }

            StoreIncumbent(built, solver, request, out incumbentAssign, out incumbentExt, out incumbentMetrics,
                out _);

            var value = (long)solver.Value(obj);
            model.Add(obj == value);

            completed.Add(rule.RuleId);
            progress?.Report(new SolveProgress(null, completed, value, $"{rule.RuleId} 完成 = {value}"));

            if (status != CpSolverStatus.Optimal)
            {
                optStatus = OptimizationStatus.TimeLimit;
                break;
            }
        }

        if (incumbentAssign is null)
        {
            var solver = CreateSolver(request, budget);
            var status = solver.Solve(model);
            if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
                return CreateInfeasible(request, budget);
            StoreIncumbent(built, solver, request, out incumbentAssign, out incumbentExt, out incumbentMetrics,
                out _);
        }

        var candidates = new List<CandidateSolutionDto>
        {
            ToCandidate(1, incumbentAssign!, incumbentExt, incumbentMetrics!, request)
        };

        var denom = built.Days.Count(kv =>
            !kv.Value.IsFixed && request.Period.IsInTargetMonth(kv.Key.Date));
        var threshold = DiversityEncoder.Threshold(denom);

        if (threshold > 0)
        {
            for (var k = 2; k <= 3; k++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (budget.Exhausted) break;

                foreach (var existing in candidates)
                {
                    var lits = LiteralsMatchingAssignment(built, existing.Assignments);
                    if (lits.Count == 0) continue;
                    var diff = DiversityEncoder.DifferenceFromIncumbent(model, lits);
                    model.Add(diff >= threshold);
                }

                // Feasibility search with fixed quality (no new objective)
                model.Minimize(model.NewConstant(0));
                var solver = CreateSolver(request, budget);
                var status = solver.Solve(model);
                if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
                    break;

                StoreIncumbent(built, solver, request, out var assign, out var ext, out var metrics, out _);
                candidates.Add(ToCandidate(k, assign!, ext, metrics!, request));
            }
        }

        return new SolveResult
        {
            ScheduleStatus = ScheduleStatus.Feasible,
            OptimizationStatus = optStatus,
            Candidates = candidates
        };
    }

    private CandidateSolutionDto ToCandidate(
        int index,
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> assignments,
        IReadOnlySet<(string Station, DateOnly Date, ShiftType Shift)>? external,
        IReadOnlyDictionary<string, int> modelMetrics,
        SolveRequest request)
    {
        var ctx = new ScheduleContext
        {
            Period = request.Period,
            Unit = request.Unit,
            Employees = request.Employees,
            Cycles = request.Cycles,
            Histories = request.Histories,
            XEvents = request.XEvents,
            Assignments = assignments,
            MonthlyShifts = request.MonthlyShifts,
            NextMonthShifts = request.NextMonthShifts,
            PreviousMonthShifts = request.PreviousMonthShifts,
            RStarRequests = request.RStarRequests,
            ExternalSlots = external ?? new HashSet<(string, DateOnly, ShiftType)>()
        };

        var evalMetrics = _evaluator.EvaluateMetrics(ctx);
        foreach (var (ruleId, modelVal) in modelMetrics)
        {
            if (!evalMetrics.TryGetValue(ruleId, out var ev)) continue;
            if (ev != modelVal)
            {
                throw new InvalidOperationException(
                    $"交叉核對失敗：{ruleId} 模型={modelVal} evaluator={ev}");
            }
        }

        return new CandidateSolutionDto
        {
            Index = index,
            IsShortageAnalysis = false,
            Assignments = assignments,
            ExternalSlots = external ?? new HashSet<(string, DateOnly, ShiftType)>(),
            ModelMetrics = modelMetrics,
            EvaluatorMetrics = evalMetrics
        };
    }

    private static void StoreIncumbent(
        BuiltModel built,
        CpSolver solver,
        SolveRequest request,
        out IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> assign,
        out IReadOnlySet<(string Station, DateOnly Date, ShiftType Shift)>? ext,
        out Dictionary<string, int> metrics,
        out List<(BoolVar Lit, string Label)> lits)
    {
        if (request.Unit == Unit.M)
        {
            var (a, e) = SolutionExtractor.ExtractM(built, solver);
            assign = a;
            ext = e;
        }
        else
        {
            assign = SolutionExtractor.ExtractT(built, solver);
            ext = null;
        }

        metrics = SolutionExtractor.ReadMetrics(built, solver).ToDictionary(kv => kv.Key, kv => kv.Value);
        lits = SolutionExtractor.IncumbentLiterals(built, solver);
    }

    private static List<(BoolVar Lit, string Label)> LiteralsMatchingAssignment(
        BuiltModel built,
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> assignments)
    {
        var list = new List<(BoolVar, string)>();
        foreach (var ((empId, date), dv) in built.Days)
        {
            if (dv.IsFixed) continue;
            if (!built.Request.Period.IsInTargetMonth(date)) continue;
            if (!assignments.TryGetValue(empId, out var dayMap) || !dayMap.TryGetValue(date, out var state))
                continue;

            if (state.IsGeneralRest)
                list.Add((dv.Rest!, $"{empId}@{date}/rest"));
            else if (state.Type == DayStateType.HolidayRest)
                list.Add((dv.R1!, $"{empId}@{date}/r1"));
            else if (state.IsNormalShift)
            {
                var home = built.Request.Employees.First(e => e.Id == empId).HomeStation;
                foreach (var (key, lit) in dv.Work)
                {
                    var match = built.Request.Unit == Unit.T
                        ? key.Shift == state.Shift
                        : key.Shift == state.Shift &&
                          key.Station == (state.Station ?? home);
                    if (match)
                    {
                        list.Add((lit, $"{empId}@{date}/work"));
                        break;
                    }
                }
            }
        }

        return list;
    }

    private static SolveResult CreateInfeasible(SolveRequest request, SolveBudget budget)
    {
        if (request.Unit == Unit.M)
        {
            var shortage = ShortageAnalyzer.Analyze(request, budget);
            return new SolveResult
            {
                ScheduleStatus = ScheduleStatus.Infeasible,
                Candidates = Array.Empty<CandidateSolutionDto>(),
                ShortageAnalysis = shortage,
                ErrorMessage = "嚴格模型無解（站務）"
            };
        }

        return new SolveResult
        {
            ScheduleStatus = ScheduleStatus.Infeasible,
            Candidates = Array.Empty<CandidateSolutionDto>(),
            TConflictSummary = TConflictSummarizer.Summarize(request),
            ErrorMessage = "嚴格模型無解（檢測）"
        };
    }

    private static CpSolver CreateSolver(SolveRequest request, SolveBudget budget)
    {
        var solver = new CpSolver();
        solver.StringParameters =
            $"max_time_in_seconds:{budget.RemainingSeconds:0.###},random_seed:{request.Seed},num_search_workers:{Math.Max(1, request.NumSearchWorkers)}";
        return solver;
    }
}
