using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Core.Abstractions;

public interface IAuditService
{
    Task<IReadOnlyList<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}
