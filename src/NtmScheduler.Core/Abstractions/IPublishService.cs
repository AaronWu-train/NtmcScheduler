using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions;

public interface IPublishService
{
    Task<IReadOnlyList<PublishBlockerDto>> CheckAsync(long draftId, CancellationToken ct = default);
    Task<long> PublishAsync(long draftId, string op, CancellationToken ct = default);
    Task<IReadOnlyList<VersionDto>> GetVersionsAsync(Unit unit, YearMonth month, CancellationToken ct = default);
    Task<WideTableDto> GetVersionAsync(long versionId, CancellationToken ct = default);
}
