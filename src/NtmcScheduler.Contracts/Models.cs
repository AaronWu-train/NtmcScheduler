namespace NtmcScheduler.Contracts;

public enum WorkspaceCode
{
    M,
    T
}

public enum PreviousScheduleSource
{
    AdoptedSchedule,
    Upload
}

public enum ScheduleRunStatus
{
    Queued,
    Running,
    Optimal,
    TimeLimit,
    Infeasible,
    InvalidInput,
    Failed,
    Imported,
    Cancelled
}

public enum ValidationSeverity
{
    Warning,
    Error
}

public enum ExternalSupportPolicy
{
    Disallowed,
    Discouraged,
    Allowed
}

public static class AuditClaimTypes
{
    public const string SessionId = "audit_session_id";
}

public sealed record ActorContext(
    Guid UserId,
    string UserName,
    bool IsAdministrator,
    IReadOnlySet<WorkspaceCode> EditableWorkspaces,
    string CorrelationId,
    Guid? SessionId = null,
    string? IpAddress = null,
    string? UserAgent = null,
    bool MustChangePassword = false)
{
    public bool CanEdit(WorkspaceCode workspace) => IsAdministrator || EditableWorkspaces.Contains(workspace);
}

public sealed record EmployeeDto(
    Guid Id,
    WorkspaceCode Workspace,
    string EmployeeCode,
    string Name,
    string Affiliation,
    DateOnly? EmploymentStartDate,
    int? Ability,
    Guid RevisionToken);

public sealed record SaveEmployeeCommand(
    Guid? Id,
    WorkspaceCode Workspace,
    string EmployeeCode,
    string Name,
    string Affiliation,
    DateOnly? EmploymentStartDate,
    int? Ability,
    Guid? RevisionToken);

public sealed record RestIntervalDto(DateOnly Start, DateOnly End, IReadOnlyList<DateOnly> NationalHolidays);
public sealed record NonStandardShiftDto(string? Name, string Code, TimeOnly StartTime, TimeOnly EndTime);
public sealed record ShiftTimePairDto(TimeOnly Start, TimeOnly End);
public sealed record WorkspaceShiftTimesDto(ShiftTimePairDto Early, ShiftTimePairDto Afternoon, ShiftTimePairDto Night);
public sealed record ConfigurationRevisionDto(
    Guid Id,
    int Version,
    DateTimeOffset CreatedAtUtc,
    Guid CurrentRevisionToken,
    IReadOnlyList<RestIntervalDto> RestIntervals,
    IReadOnlyList<NonStandardShiftDto> NonStandardShifts,
    WorkspaceShiftTimesDto MShiftTimes,
    WorkspaceShiftTimesDto TShiftTimes);

public sealed record StaffingRangeDto(int Minimum, int Maximum);
public sealed record MStationSettingDto(
    string Code,
    string Group,
    ExternalSupportPolicy ExternalSupport,
    StaffingRangeDto Early,
    StaffingRangeDto Afternoon,
    StaffingRangeDto Night);
public sealed record MonthlySchedulingSettingsDto(
    int GeneralRestTarget,
    int SpecialRestTarget,
    int GeneralRestMinimum,
    int GeneralRestMaximum,
    int SpecialRestMinimum,
    int SpecialRestMaximum,
    IReadOnlyList<MStationSettingDto> MStations);

public sealed record DemandEmployeeDto(
    Guid Id,
    string EmployeeCode,
    string Name,
    string Affiliation,
    DateOnly? EmploymentStartDate,
    int? Ability,
    string? MonthlyShift,
    int? OpeningRest,
    int? OpeningSpecialRest,
    int RequestedLeaveRestCount,
    string? PerpetualScheduleId,
    IReadOnlyList<string> MonthlyCsvValues,
    IReadOnlyList<DemandAssignmentDto> Assignments);

public sealed record DemandAssignmentDto(
    Guid Id,
    Guid DemandEmployeeId,
    DateOnly Date,
    string? Kind,
    bool RequestedRest,
    string? Station,
    string? Shift,
    DateTimeOffset? EventStart,
    DateTimeOffset? EventEnd,
    string? EventDescription);

public sealed record DemandDraftDto(
    Guid Id,
    WorkspaceCode Workspace,
    DateOnly Month,
    PreviousScheduleSource PreviousSource,
    bool HasUploadedPreviousSchedule,
    Guid? PreviousScheduleVersionId,
    Guid ConfigurationRevisionId,
    Guid RevisionToken,
    DateTimeOffset UpdatedAtUtc,
    PreviousUploadDto? PreviousUpload,
    PerpetualUploadDto? PerpetualUpload,
    MonthlySchedulingSettingsDto MonthlySettings,
    IReadOnlyList<DemandEmployeeDto> Employees);

public sealed record PreviousUploadDto(string FileName, DateTimeOffset UploadedAtUtc);
public sealed record PreviousSchedulePreviewDto(WorkspaceCode Workspace, DateOnly Month, IReadOnlyList<PreviousScheduleEmployeeDto> Employees);
public sealed record PreviousScheduleEmployeeDto(string EmployeeCode, string Name, string Affiliation, IReadOnlyList<string> MonthlyCsvValues);
public sealed record PreviousScheduleFileDto(string FileName, byte[] Content);
public sealed record PerpetualUploadDto(string FileName, DateTimeOffset UploadedAtUtc, bool IsEmpty);
public sealed record PerpetualScheduleFileDto(string FileName, byte[] Content);
public sealed record MPerpetualPatternDto(string Id, IReadOnlyList<string> Days, int EarlyCount, int AfternoonCount, int NightCount);
public sealed record MPerpetualScheduleDto(string FileName, DateTimeOffset UpdatedAtUtc, Guid RevisionToken, IReadOnlyList<MPerpetualPatternDto> Patterns);

public sealed record ImportPreviewDto(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Differences);
public sealed record EmployeeImportPreviewDto(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Differences, Guid RevisionToken);

public sealed record EmployeeDemandSubmissionAssignmentDto(
    Guid Id,
    DateOnly Date,
    string? Kind,
    bool RequestedRest,
    string? Station,
    string? Shift,
    DateTimeOffset? EventStart,
    DateTimeOffset? EventEnd,
    string? EventDescription);

public sealed record EmployeeDemandSubmissionDto(
    Guid Id,
    WorkspaceCode Workspace,
    DateOnly Month,
    string EmployeeCode,
    string Name,
    string Affiliation,
    DateOnly? EmploymentStartDate,
    int RequestedLeaveRestCount,
    Guid RevisionToken,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedByName,
    bool IsLate,
    IReadOnlyList<EmployeeDemandSubmissionAssignmentDto> Assignments);

public sealed record DemandSubmissionImportDto(
    Guid DemandDraftId,
    DateTimeOffset ImportedAtUtc,
    string ImportedByName);

public sealed record SubmissionImportPreviewDto(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Differences,
    int SubmissionCount,
    int MatchedEmployeeCount,
    int LateSubmissionCount);

public sealed record ScheduleVersionDto(
    Guid Id,
    WorkspaceCode Workspace,
    DateOnly Month,
    string Name,
    ScheduleRunStatus SourceStatus,
    bool IsAdopted,
    bool IsArchived,
    bool HasErrors,
    int WarningCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid RevisionToken,
    Guid ConfigurationRevisionId,
    int? GeneralRestTarget = null,
    int? SpecialRestTarget = null,
    IReadOnlyList<ObjectiveScoreDto>? Objectives = null);

public sealed record ScheduleMonthDto(DateOnly Month, int VersionCount, ScheduleVersionDto? Adopted, DateTimeOffset UpdatedAtUtc);

public sealed record ScheduleAssignmentDto(
    Guid Id,
    Guid EmployeeSnapshotId,
    string EmployeeCode,
    string EmployeeName,
    DateOnly Date,
    string Kind,
    bool RequestedRest,
    string? Station,
    string? Shift,
    DateTimeOffset? EventStart,
    DateTimeOffset? EventEnd,
    string? EventDescription);

public sealed record ScheduleEmployeeStats(
    string EmployeeCode,
    int Rest,
    int SpecialRest,
    int LeaveRest,
    int WeekdayWork,
    int HolidayWork,
    int Early,
    int Afternoon,
    int Night,
    int Other);

public sealed record ScheduleEmployeeInfoDto(
    Guid Id,
    string EmployeeCode,
    string Name,
    string Affiliation,
    DateOnly? EmploymentStartDate,
    int? Ability,
    string? MonthlyShift,
    int? OpeningRest,
    int? OpeningSpecialRest,
    IReadOnlyList<string> MonthlyCsvValues);

public sealed record ExternalAssignmentDto(DateOnly Date, string Station, string Shift, int Count);

public sealed record ScheduleIntervalStatsDto(
    string EmployeeCode,
    DateOnly Start,
    DateOnly End,
    int Rest,
    int SpecialRest,
    int RequiredSpecialRest);

public sealed record ScheduleCoverageDto(
    DateOnly Date,
    string Station,
    string Shift,
    int Required,
    int Maximum,
    bool AllowsMultiple,
    int Internal,
    int External);

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string RuleName,
    string Message,
    string? EmployeeCode = null,
    DateOnly? Date = null,
    string? Station = null,
    string? Shift = null,
    bool IsLaborLawViolation = false);

public sealed record ScheduleDetailDto(
    ScheduleVersionDto Version,
    IReadOnlyList<ScheduleEmployeeInfoDto> Employees,
    IReadOnlyList<ScheduleAssignmentDto> Assignments,
    IReadOnlyList<ExternalAssignmentDto> ExternalAssignments,
    IReadOnlyList<ScheduleEmployeeStats> EmployeeStats,
    IReadOnlyList<ScheduleIntervalStatsDto> IntervalStats,
    IReadOnlyList<ScheduleCoverageDto> Coverage,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<ScheduleSuggestionDto>? Suggestions = null);

public sealed record ScheduleSuggestionDto(string Name, long Value, IReadOnlyList<ScheduleSuggestionLocationDto> Locations);
public sealed record ScheduleSuggestionLocationDto(string Label, string? EmployeeCode = null, DateOnly? Date = null);

public sealed record ScheduleRunDto(
    Guid Id,
    WorkspaceCode Workspace,
    DateOnly Month,
    ScheduleRunStatus Status,
    string? Error,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TimeLimitSeconds,
    int WorkerCount,
    int SeedCount,
    IReadOnlyDictionary<string, int> RuleWeights,
    IReadOnlyList<ScheduleRunCandidateDto> Candidates);

public sealed record ScheduleRunCandidateDto(int Number, IReadOnlyList<ObjectiveScoreDto> Objectives);
public sealed record ObjectiveScoreDto(int Priority, string Name, long Value, IReadOnlyList<ObjectiveComponentDto> Components);
public sealed record ObjectiveComponentDto(string Name, long Value, int Weight);

public sealed record ScheduleRunOptions(int TimeLimitSeconds, int WorkerCount, int SeedCount, Dictionary<string, int>? RuleWeights = null)
{
    public const int MaxTimeLimitSeconds = 600;
    public const int MaxWorkerCount = 8;
    public const int MaxSeedCount = 4;
}

public sealed record SolverRuleDefinitionDto(string Key, string Name, string Description, int Priority, bool IsHard, int? DefaultWeight);

public sealed record AuditFieldChangeDto(string Label, string? Before, string? After);

public sealed record AuditTechnicalDetailsDto(
    Guid? ActorUserId,
    Guid? SessionId,
    string Action,
    string ResourceType,
    string ResourceId,
    string? BeforeJson,
    string? AfterJson,
    string? IpAddress,
    string? UserAgent,
    string CorrelationId);

public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset AtUtc,
    string ActorName,
    Guid? ActorUserId,
    Guid? SessionId,
    string? IpAddress,
    string? UserAgent,
    string Action,
    string ActionLabel,
    WorkspaceCode? Workspace,
    string TargetSummary,
    string ReadableSummary,
    bool Succeeded,
    string CorrelationId,
    IReadOnlyList<AuditFieldChangeDto> Changes,
    AuditTechnicalDetailsDto Technical);

public sealed record UserAccountDto(Guid Id, string UserName, bool IsDisabled, bool MustChangePassword, bool IsAdministrator, IReadOnlySet<WorkspaceCode> EditableWorkspaces, Guid RevisionToken);
public sealed record CreateUserCommand(string UserName, string TemporaryPassword, bool IsAdministrator, IReadOnlySet<WorkspaceCode> EditableWorkspaces);

public sealed class ConcurrencyConflictException(string message) : Exception(message);
public sealed class ForbiddenOperationException(string message) : Exception(message);
public sealed class DomainValidationException(string message) : Exception(message);
