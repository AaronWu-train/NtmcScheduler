using Microsoft.AspNetCore.Identity;
using NtmcScheduler.Contracts;

namespace NtmcScheduler.Infrastructure.Data;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool MustChangePassword { get; set; } = true;
    public bool IsDisabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<WorkspacePermission> WorkspacePermissions { get; set; } = [];
}

public sealed class WorkspacePermission
{
    public Guid UserId { get; set; }
    public WorkspaceCode Workspace { get; set; }
    public ApplicationUser User { get; set; } = null!;
}

public sealed class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkspaceCode Workspace { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Affiliation { get; set; } = "";
    public DateOnly? EmploymentStartDate { get; set; }
    public int? Ability { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid RevisionToken { get; set; } = Guid.NewGuid();
}

public sealed class ConfigurationRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<RestIntervalEntity> RestIntervals { get; set; } = [];
    public List<NonStandardShiftEntity> NonStandardShifts { get; set; } = [];
}

public sealed class CurrentConfiguration
{
    public int Id { get; set; } = 1;
    public Guid ConfigurationRevisionId { get; set; }
    public Guid RevisionToken { get; set; } = Guid.NewGuid();
    public ConfigurationRevision ConfigurationRevision { get; set; } = null!;
}

public sealed class RestIntervalEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConfigurationRevisionId { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public ConfigurationRevision ConfigurationRevision { get; set; } = null!;
    public List<NationalHoliday> NationalHolidays { get; set; } = [];
}

public sealed class NationalHoliday
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestIntervalId { get; set; }
    public DateOnly Date { get; set; }
    public RestIntervalEntity RestInterval { get; set; } = null!;
}

public sealed class NonStandardShiftEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConfigurationRevisionId { get; set; }
    public string? Name { get; set; }
    public string Code { get; set; } = "";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public ConfigurationRevision ConfigurationRevision { get; set; } = null!;
}

public sealed class DemandDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkspaceCode Workspace { get; set; }
    public DateOnly Month { get; set; }
    public PreviousScheduleSource PreviousSource { get; set; }
    public Guid? PreviousAdoptedScheduleVersionId { get; set; }
    public Guid? UploadedPreviousScheduleId { get; set; }
    public Guid ConfigurationRevisionId { get; set; }
    public string? PerpetualScheduleJson { get; set; }
    public string? PerpetualScheduleFileName { get; set; }
    public DateTimeOffset? PerpetualScheduleUploadedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid RevisionToken { get; set; } = Guid.NewGuid();
    public ConfigurationRevision ConfigurationRevision { get; set; } = null!;
    public UploadedPreviousSchedule? UploadedPreviousSchedule { get; set; }
    public List<DemandEmployee> Employees { get; set; } = [];
}

public sealed class DemandEmployee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DemandDraftId { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Affiliation { get; set; } = "";
    public DateOnly? EmploymentStartDate { get; set; }
    public int? Ability { get; set; }
    public string? MonthlyShift { get; set; }
    public int? OpeningRest { get; set; }
    public int? OpeningSpecialRest { get; set; }
    public int RequestedLeaveRestCount { get; set; }
    public string? PerpetualScheduleId { get; set; }
    public DemandDraft DemandDraft { get; set; } = null!;
    public List<DemandAssignment> Assignments { get; set; } = [];
}

public sealed class DemandAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DemandEmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public string? Kind { get; set; }
    public bool RequestedRest { get; set; }
    public string? Station { get; set; }
    public string? Shift { get; set; }
    public DateTimeOffset? EventStart { get; set; }
    public DateTimeOffset? EventEnd { get; set; }
    public string? EventDescription { get; set; }
    public DemandEmployee DemandEmployee { get; set; } = null!;
}

public sealed class UploadedPreviousSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkspaceCode Workspace { get; set; }
    public DateOnly Month { get; set; }
    public string FileName { get; set; } = "";
    public string ParsedScheduleJson { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ScheduleRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkspaceCode Workspace { get; set; }
    public DateOnly Month { get; set; }
    public ScheduleRunStatus Status { get; set; } = ScheduleRunStatus.Queued;
    public Guid DemandDraftId { get; set; }
    public Guid? ConfigurationRevisionId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string RequestedByName { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public int RandomSeed { get; set; }
    public int WorkerCount { get; set; }
    public int SeedCount { get; set; } = 1;
    public int TimeLimitSeconds { get; set; }
    public string ProgramVersion { get; set; } = "";
    public string InputHash { get; set; } = "";
    public string InputSnapshotJson { get; set; } = "";
    public string? PerpetualScheduleJson { get; set; }
    public string? ResultDetailsJson { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public List<ScheduleVersion> Versions { get; set; } = [];
}

public sealed class ScheduleVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkspaceCode Workspace { get; set; }
    public DateOnly Month { get; set; }
    public string Name { get; set; } = "";
    public Guid? SourceRunId { get; set; }
    public int? CandidateIndex { get; set; }
    public ScheduleRunStatus SourceStatus { get; set; }
    public Guid ConfigurationRevisionId { get; set; }
    public bool HasErrors { get; set; }
    public int WarningCount { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Guid RevisionToken { get; set; } = Guid.NewGuid();
    public ScheduleRun? SourceRun { get; set; }
    public ConfigurationRevision ConfigurationRevision { get; set; } = null!;
    public List<ScheduleEmployeeSnapshot> Employees { get; set; } = [];
    public List<ExternalAssignment> ExternalAssignments { get; set; } = [];
}

public sealed class ScheduleEmployeeSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleVersionId { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Affiliation { get; set; } = "";
    public DateOnly? EmploymentStartDate { get; set; }
    public int? Ability { get; set; }
    public string? MonthlyShift { get; set; }
    public int? OpeningRest { get; set; }
    public int? OpeningSpecialRest { get; set; }
    public int RequestedLeaveRestCount { get; set; }
    public int? ClosingRest { get; set; }
    public int? ClosingSpecialRest { get; set; }
    public int? NormalWorkCount { get; set; }
    public string? PerpetualScheduleId { get; set; }
    public ScheduleVersion ScheduleVersion { get; set; } = null!;
    public List<ScheduleAssignment> Assignments { get; set; } = [];
}

public sealed class ScheduleAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleEmployeeSnapshotId { get; set; }
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = "";
    public bool RequestedRest { get; set; }
    public string? Station { get; set; }
    public string? Shift { get; set; }
    public DateTimeOffset? EventStart { get; set; }
    public DateTimeOffset? EventEnd { get; set; }
    public string? EventDescription { get; set; }
    public ScheduleEmployeeSnapshot Employee { get; set; } = null!;
}

public sealed class ExternalAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleVersionId { get; set; }
    public DateOnly Date { get; set; }
    public string Station { get; set; } = "";
    public string Shift { get; set; } = "";
    public int Count { get; set; }
    public ScheduleVersion ScheduleVersion { get; set; } = null!;
}

public sealed class AdoptedSchedule
{
    public WorkspaceCode Workspace { get; set; }
    public DateOnly Month { get; set; }
    public Guid ScheduleVersionId { get; set; }
    public Guid AdoptedByUserId { get; set; }
    public DateTimeOffset AdoptedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ScheduleVersion ScheduleVersion { get; set; } = null!;
}

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long AtUtcTicks { get; set; } = DateTimeOffset.UtcNow.UtcTicks;
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public WorkspaceCode? Workspace { get; set; }
    public string ResourceType { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public bool Succeeded { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string CorrelationId { get; set; } = "";
}
