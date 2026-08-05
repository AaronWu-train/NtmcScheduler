using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Core.Abstractions;

public interface IHistoryImportService
{
    Task<ImportResult> ImportAsync(Stream scheduleCsv, Stream? eventsCsv, string op, CancellationToken ct = default);
}
