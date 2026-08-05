using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions;

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeInfo>> ListAsync(Unit unit, CancellationToken ct = default);
    Task UpsertAsync(EmployeeInfo employee, string op, CancellationToken ct = default);
    Task DeleteAsync(string id, string op, CancellationToken ct = default);
    Task<ImportResult> ImportCsvAsync(Unit unit, Stream csv, string op, CancellationToken ct = default);
    Task<byte[]> ExportCsvAsync(Unit unit, CancellationToken ct = default);
}
