using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions;

public interface IPreparationService
{
    Task<PreparationStatusDto> GetStatusAsync(Unit unit, YearMonth month, CancellationToken ct = default);
}