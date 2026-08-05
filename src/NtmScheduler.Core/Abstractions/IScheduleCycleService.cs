using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions;

public interface IScheduleCycleService
{
    Task<IReadOnlyList<CycleInfo>> ListAsync(CancellationToken ct = default);
    Task UpsertAsync(CycleInfo cycle, string op, CancellationToken ct = default);
    Task DeleteAsync(DateOnly start, string op, CancellationToken ct = default);
}
