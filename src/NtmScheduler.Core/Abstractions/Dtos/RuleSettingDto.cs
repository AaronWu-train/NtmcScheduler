using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions.Dtos;

public sealed record RuleSettingDto(
    long Id,
    Unit Unit,
    string RuleId,
    int Priority,
    bool Enabled,
    int Order,
    string? ParametersJson);
