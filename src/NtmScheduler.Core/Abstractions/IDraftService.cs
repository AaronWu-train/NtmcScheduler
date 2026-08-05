using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions;

public interface IDraftService
{
    Task<WideTableDto> GetAsync(long draftId, CancellationToken ct = default);
    Task<IReadOnlyList<CellOptionDto>> GetCellOptionsAsync(
        long draftId, string employeeId, DateOnly date, CancellationToken ct = default);
    Task<DraftValidationDto> ApplyEditAsync(
        long draftId, string employeeId, DateOnly date, DayState state, string op,
        CancellationToken ct = default);
    Task<DraftValidationDto> UndoAsync(long draftId, string op, CancellationToken ct = default);
    Task<DraftValidationDto> RevalidateAsync(long draftId, CancellationToken ct = default);
}
