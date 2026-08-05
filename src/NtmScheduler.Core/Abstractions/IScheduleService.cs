using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions;

public interface IScheduleService
{
    Task<WideTableDto?> GetCurrentAsync(Unit unit, YearMonth month, CancellationToken ct = default);
    Task<WideTableDto> GetAsync(long scheduleId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Unit unit, YearMonth month, CancellationToken ct = default);

    /// <summary>
    /// Copy a candidate into the current month schedule. Replaces any existing schedule for that unit/month.
    /// </summary>
    Task<long> SelectCandidateAsync(long candidateId, string op, CancellationToken ct = default);

    Task<IReadOnlyList<CellOptionDto>> GetCellOptionsAsync(
        long scheduleId, string employeeId, DateOnly date, CancellationToken ct = default);
    Task<ScheduleValidationDto> ApplyEditAsync(
        long scheduleId, string employeeId, DateOnly date, DayState state, string op,
        CancellationToken ct = default);
    Task<ScheduleValidationDto> UndoAsync(long scheduleId, string op, CancellationToken ct = default);
    Task<ScheduleValidationDto> RevalidateAsync(long scheduleId, CancellationToken ct = default);

    Task<long> CreateSnapshotAsync(long scheduleId, string op, CancellationToken ct = default);
    Task<IReadOnlyList<VersionDto>> GetSnapshotsAsync(Unit unit, YearMonth month, CancellationToken ct = default);
    Task<WideTableDto> GetSnapshotAsync(long snapshotId, CancellationToken ct = default);
    Task RestoreSnapshotAsync(long snapshotId, string op, CancellationToken ct = default);
}
