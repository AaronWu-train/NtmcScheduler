using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Core.Abstractions;

public interface ISolveService
{
    Task<SolveResult> SolveAsync(
        SolveRequest request,
        IProgress<SolveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
