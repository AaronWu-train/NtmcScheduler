using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Validation;

namespace NtmScheduler.Core.Abstractions;

public interface IEventService
{
    Task<IReadOnlyList<FixedEventDto>> ListAsync(Unit unit, YearMonth month, CancellationToken ct = default);
    Task<ValidationError[]> AddRStarAsync(string employeeId, DateOnly date, string op, CancellationToken ct = default);
    Task<ValidationError[]> AddXAsync(XEvent x, string op, CancellationToken ct = default);
    Task DeleteAsync(long eventId, string op, CancellationToken ct = default);
    Task<ImportResult> ImportCsvAsync(Stream csv, string op, CancellationToken ct = default);
    Task<byte[]> ExportCsvAsync(Unit? unit = null, YearMonth? month = null, CancellationToken ct = default);
}
