using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Solvers;

public sealed class SolveService : ISolveService
{
    private readonly LexicographicSolveEngine _engine = new();

    public Task<SolveResult> SolveAsync(
        SolveRequest request,
        IProgress<SolveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _engine.Solve(request, progress, cancellationToken), cancellationToken);
    }
}
