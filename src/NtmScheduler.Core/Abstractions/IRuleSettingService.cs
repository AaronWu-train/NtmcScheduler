using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions;

public interface IRuleSettingService
{
    Task<IReadOnlyList<RuleSettingDto>> GetAsync(Unit unit, CancellationToken ct = default);
    Task UpdateAsync(Unit unit, IReadOnlyList<RuleSettingDto> ordered, string op, CancellationToken ct = default);
}
