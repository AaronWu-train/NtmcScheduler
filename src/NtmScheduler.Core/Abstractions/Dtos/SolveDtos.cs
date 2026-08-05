using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Abstractions.Dtos;

public enum ScheduleStatus
{
    Feasible,
    Infeasible,
    InvalidInput
}

public enum OptimizationStatus
{
    Optimal,
    TimeLimit
}

public sealed record SoftRuleSpec(
    string RuleId,
    int Order,
    bool Enabled,
    string? ParametersJson = null);

public sealed class SolveRequest
{
    public required Unit Unit { get; init; }
    public required SchedulePeriod Period { get; init; }
    public required IReadOnlyList<EmployeeInfo> Employees { get; init; }
    public required IReadOnlyList<CycleInfo> Cycles { get; init; }
    public required IReadOnlyDictionary<string, EmployeeHistory> Histories { get; init; }
    public required IReadOnlyList<XEvent> XEvents { get; init; }
    public IReadOnlyList<(string EmployeeId, DateOnly Date)> RStarRequests { get; init; } =
        Array.Empty<(string, DateOnly)>();

    /// <summary>
    /// Fixed assignments inside the scheduling period (published days / X).
    /// History before FirstDay lives in <see cref="Histories"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> FixedAssignments { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<DateOnly, DayState>>();

    public required IReadOnlyList<SoftRuleSpec> SoftRules { get; init; }
    public IReadOnlyDictionary<string, ShiftType>? MonthlyShifts { get; init; }
    public IReadOnlyDictionary<string, ShiftType>? NextMonthShifts { get; init; }
    public IReadOnlyDictionary<string, ShiftType>? PreviousMonthShifts { get; init; }
    public int Seed { get; init; }
    public TimeSpan TotalTimeLimit { get; init; } = TimeSpan.FromMinutes(5);
    public int NumSearchWorkers { get; init; } = 4;
}

public sealed record SolveProgress(
    string? CurrentRuleId,
    IReadOnlyList<string> CompletedRuleIds,
    long? ObjectiveBound,
    string? Message);

public sealed class CandidateSolutionDto
{
    public int Index { get; init; }
    public bool IsShortageAnalysis { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> Assignments { get; init; }
    public IReadOnlySet<(string Station, DateOnly Date, ShiftType Shift)> ExternalSlots { get; init; } =
        new HashSet<(string, DateOnly, ShiftType)>();
    public required IReadOnlyDictionary<string, int> ModelMetrics { get; init; }
    public IReadOnlyDictionary<string, int>? EvaluatorMetrics { get; init; }
    public double? DiversityRate { get; init; }
}

public sealed class TConflictSummaryDto
{
    public required string Message { get; init; }
    public required IReadOnlyList<CycleRestStatDto> CycleStats { get; init; }
    public required IReadOnlyDictionary<ShiftType, int> GroupSizes { get; init; }
    public int TotalRStarRequests { get; init; }
}

public sealed record CycleRestStatDto(
    DateOnly Start,
    DateOnly End,
    int RequiredR,
    int RequiredR1,
    int RemainingDaysAfterRange,
    int EmployeeCount);

public sealed class SolveResult
{
    public required ScheduleStatus ScheduleStatus { get; init; }
    public OptimizationStatus? OptimizationStatus { get; init; }
    public IReadOnlyList<CandidateSolutionDto> Candidates { get; init; } = Array.Empty<CandidateSolutionDto>();
    public CandidateSolutionDto? ShortageAnalysis { get; init; }
    public TConflictSummaryDto? TConflictSummary { get; init; }
    public string? ErrorMessage { get; init; }
    public bool ShortageAnalysisAvailable => ShortageAnalysis is not null;
}
