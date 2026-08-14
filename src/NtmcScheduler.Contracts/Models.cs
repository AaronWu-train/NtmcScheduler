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
    Failed
}

public enum ValidationSeverity
{
    Warning,
    Error
}

public sealed record ActorContext(
    Guid UserId,
    string UserName,
    bool IsAdministrator,
    IReadOnlySet<WorkspaceCode> EditableWorkspaces,
    string CorrelationId,
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
public sealed record ConfigurationRevisionDto(
    Guid Id,
    int Version,
    DateTimeOffset CreatedAtUtc,
    Guid CurrentRevisionToken,
    IReadOnlyList<RestIntervalDto> RestIntervals,
    IReadOnlyList<NonStandardShiftDto> NonStandardShifts);

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
    Guid ConfigurationRevisionId,
    Guid RevisionToken,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DemandEmployeeDto> Employees);

public sealed record ImportPreviewDto(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Differences);
public sealed record EmployeeImportPreviewDto(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Differences, Guid RevisionToken);

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
    Guid ConfigurationRevisionId);

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
    int? Ability,
    string? MonthlyShift);

public sealed record ExternalAssignmentDto(DateOnly Date, string Station, string Shift, int Count);

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string RuleName,
    string Message,
    string? EmployeeCode = null,
    DateOnly? Date = null,
    string? Station = null,
    string? Shift = null);

public sealed record ScheduleDetailDto(
    ScheduleVersionDto Version,
    IReadOnlyList<ScheduleEmployeeInfoDto> Employees,
    IReadOnlyList<ScheduleAssignmentDto> Assignments,
    IReadOnlyList<ExternalAssignmentDto> ExternalAssignments,
    IReadOnlyList<ScheduleEmployeeStats> EmployeeStats,
    IReadOnlyList<ValidationIssue> Issues);

public sealed record ScheduleRunDto(
    Guid Id,
    WorkspaceCode Workspace,
    DateOnly Month,
    ScheduleRunStatus Status,
    string? Error,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset AtUtc,
    string ActorName,
    string Action,
    WorkspaceCode? Workspace,
    string ResourceType,
    string ResourceId,
    bool Succeeded,
    string CorrelationId);

public sealed record UserAccountDto(Guid Id, string UserName, bool IsDisabled, bool MustChangePassword, bool IsAdministrator, IReadOnlySet<WorkspaceCode> EditableWorkspaces, Guid RevisionToken);
public sealed record CreateUserCommand(string UserName, string TemporaryPassword, bool IsAdministrator, IReadOnlySet<WorkspaceCode> EditableWorkspaces);

public sealed class ConcurrencyConflictException(string message) : Exception(message);
public sealed class ForbiddenOperationException(string message) : Exception(message);
public sealed class DomainValidationException(string message) : Exception(message);
