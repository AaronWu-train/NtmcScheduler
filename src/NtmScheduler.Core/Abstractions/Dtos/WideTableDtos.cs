using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;

namespace NtmScheduler.Core.Abstractions.Dtos;

public sealed record CellDto(
    DateOnly Date,
    DayState State,
    bool IsExtensionDay,
    bool IsEditable,
    IReadOnlyList<string> ViolationRuleIds,
    DateTime? XStart = null,
    DateTime? XEnd = null,
    string? XDescription = null)
{
    public string StateDisplay => State.ToDisplay();
}

public sealed record WideTableRowDto(
    string EmployeeId,
    string EmployeeName,
    string? StationOrGroup,
    string? ShiftGroup,
    IReadOnlyDictionary<DateOnly, CellDto> Cells,
    RestStatsDto? RestStats = null);

public sealed record WideTableDto(
    Unit Unit,
    YearMonth TargetMonth,
    DateOnly MonthEnd,
    IReadOnlyList<DateOnly> Dates,
    IReadOnlyList<WideTableRowDto> Rows,
    bool IsEditable = false,
    long? OwnerId = null);

public sealed record CellOptionDto(
    DayState State,
    IReadOnlyList<string> P0ViolationsIfApplied)
{
    public string StateDisplay => State.ToDisplay();
}

public sealed record RuleMetricDto(string RuleId, int ViolationCount, bool IsHard);

public sealed record PublishBlockerDto(
    string Code,
    string Message,
    string? EmployeeId = null,
    DateOnly? Date = null);

public sealed record DraftValidationDto(
    bool P0Passed,
    IReadOnlyList<RuleMetricDto> RuleMetrics,
    IReadOnlyList<MCoverageRow>? MCoverage,
    IReadOnlyList<TCoverageRow>? TCoverage,
    IReadOnlyList<PublishBlockerDto> PublishBlockers,
    IReadOnlyList<ViolationItem> Violations);
