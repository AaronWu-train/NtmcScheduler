using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions;

public interface IMonthlyShiftService
{
    Task<IReadOnlyDictionary<string, ShiftType>> GetMonthAsync(YearMonth month, CancellationToken ct = default);
    Task UpsertAsync(string employeeId, YearMonth month, ShiftType shift, string op, CancellationToken ct = default);
    Task DeleteAsync(string employeeId, YearMonth month, string op, CancellationToken ct = default);
    Task<ImportResult> ImportCsvAsync(Stream csv, string op, CancellationToken ct = default);
    Task<byte[]> ExportCsvAsync(YearMonth? month = null, CancellationToken ct = default);
}
