using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Core.Abstractions;

public interface IShortageAnalysisService
{
    Task<ShortageDto?> GetAsync(long runId, CancellationToken ct = default);
}