using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions.Dtos;

public sealed record PreparationItemDto(
    string Key,
    string Label,
    bool IsComplete,
    IReadOnlyList<string> MissingDetails);

public sealed record PreparationStatusDto(
    Unit Unit,
    YearMonth TargetMonth,
    IReadOnlyList<PreparationItemDto> Items)
{
    public bool IsReady => Items.All(i => i.IsComplete);
}
