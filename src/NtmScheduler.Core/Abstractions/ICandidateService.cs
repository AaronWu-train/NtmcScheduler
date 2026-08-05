using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Core.Abstractions;

public interface ICandidateService
{
    Task<IReadOnlyList<CandidateDto>> GetAsync(long runId, CancellationToken ct = default);
    Task<CandidateCompareDto> CompareAsync(long runId, CancellationToken ct = default);
    Task<long> PromoteToDraftAsync(long candidateId, string op, CancellationToken ct = default);
}
