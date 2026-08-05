using Google.OrTools.Sat;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Solvers.Common;
using NtmScheduler.Solvers.M;

namespace NtmScheduler.Solvers;

/// <summary>
/// M shortage analysis: relax only coverage (M-H-02) with slack, minimize Σslack.
/// </summary>
public static class ShortageAnalyzer
{
    public static CandidateSolutionDto? Analyze(SolveRequest request, SolveBudget budget)
    {
        var builder = new MModelBuilder(request, allowShortage: true);
        var built = builder.Build();
        if (!built.SoftObjectives.TryGetValue("SHORTAGE", out var obj))
            return null;

        built.Model.Minimize(obj);
        var solver = new CpSolver();
        solver.StringParameters =
            $"max_time_in_seconds:{budget.RemainingSeconds:0.###},random_seed:{request.Seed},num_search_workers:{request.NumSearchWorkers}";

        var status = solver.Solve(built.Model);
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            return null;

        var (assignments, external) = SolutionExtractor.ExtractM(built, solver);
        var metrics = new Dictionary<string, int>
        {
            ["SHORTAGE"] = (int)solver.Value(obj)
        };

        return new CandidateSolutionDto
        {
            Index = 0,
            IsShortageAnalysis = true,
            Assignments = assignments,
            ExternalSlots = external,
            ModelMetrics = metrics
        };
    }
}
