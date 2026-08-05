using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Validation;

namespace NtmScheduler.Core.Abstractions.Dtos;

public enum RunLifecycleStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

public sealed record CreateRunResult(
    long? RunId,
    IReadOnlyList<ValidationError> Errors)
{
    public bool Succeeded => RunId is not null && Errors.Count == 0;

    public static CreateRunResult Ok(long runId) =>
        new(runId, Array.Empty<ValidationError>());

    public static CreateRunResult Invalid(params ValidationError[] errors) =>
        new(null, errors);
}

public sealed record RunProgressDto(
    long RunId,
    RunLifecycleStatus Lifecycle,
    ScheduleStatus? ScheduleStatus,
    OptimizationStatus? OptimizationStatus,
    string? CurrentRuleId,
    IReadOnlyList<string> CompletedRuleIds,
    int CandidateCount,
    bool ShortageAnalysisAvailable = false,
    string? ErrorMessage = null)
{
    public long Id => RunId;
    public RunLifecycleStatus Status => Lifecycle;
}

public sealed record RunSummaryDto(
    long RunId,
    Unit Unit,
    YearMonth TargetMonth,
    RunLifecycleStatus Lifecycle,
    ScheduleStatus? ScheduleStatus,
    DateTime CreatedAt,
    string Operator,
    OptimizationStatus? OptimizationStatus = null,
    int CandidateCount = 0)
{
    public long Id => RunId;
    public RunLifecycleStatus Status => Lifecycle;
}

public sealed record CandidateDto(
    long Id,
    long RunId,
    int Index,
    bool IsShortageAnalysis,
    IReadOnlyDictionary<string, int> RuleMetrics,
    double? DiversityRate,
    string? MetricsJson = null);

public sealed record CandidateCompareDto(
    IReadOnlyList<string> RuleIds,
    IReadOnlyList<CandidateDto> Candidates,
    IReadOnlyDictionary<string, IReadOnlyList<int>>? ViolationMatrix = null,
    IReadOnlyList<double>? PairwiseDiversityRates = null,
    IReadOnlyList<global::NtmScheduler.Core.Evaluation.MCoverageRow>? MCoverageSummary = null,
    IReadOnlyList<global::NtmScheduler.Core.Evaluation.TCoverageRow>? TCoverageSummary = null);

public sealed record VersionDto(
    long Id,
    Unit Unit,
    YearMonth Month,
    int VersionNo,
    DateTime PublishedAt,
    string Operator,
    bool IsCurrent);

public sealed record ShortageDto(
    long RunId,
    long? CandidateId,
    WideTableDto? Table,
    IReadOnlyList<global::NtmScheduler.Core.Evaluation.MCoverageRow> Coverage,
    string Summary);
