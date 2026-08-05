using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions;

public interface IScheduleRunService
{
    Task<CreateRunResult> CreateAsync(Unit unit, YearMonth month, string op, CancellationToken ct = default);
    Task<RunProgressDto> GetProgressAsync(long runId, CancellationToken ct = default);
    Task<IReadOnlyList<RunSummaryDto>> ListAsync(Unit? unit = null, CancellationToken ct = default);
}
