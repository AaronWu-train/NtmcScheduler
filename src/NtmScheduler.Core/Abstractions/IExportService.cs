using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Core.Abstractions;

public interface IExportService
{
    Task<byte[]> ScheduleCsvAsync(OwnerRef solution, CancellationToken ct = default);
    Task<byte[]> CoverageCsvAsync(OwnerRef solution, CancellationToken ct = default);
    Task<byte[]> ViolationsCsvAsync(OwnerRef solution, CancellationToken ct = default);
    Task<string> ResultJsonAsync(long runId, CancellationToken ct = default);
}
