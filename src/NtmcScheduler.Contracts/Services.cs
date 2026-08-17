namespace NtmcScheduler.Contracts;

public interface IUserAdministrationService
{
    Task<IReadOnlyList<UserAccountDto>> ListAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<UserAccountDto> CreateAsync(CreateUserCommand command, ActorContext actor, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(Guid userId, string temporaryPassword, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid userId, bool isDisabled, bool isAdministrator, IReadOnlySet<WorkspaceCode> workspaces, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface ICommonConfigurationService
{
    Task<ConfigurationRevisionDto?> GetCurrentAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<ConfigurationRevisionDto?> GetRevisionAsync(Guid id, ActorContext actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RestIntervalDto>> ParseRestIntervalsCsvAsync(Stream csv, ActorContext actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NonStandardShiftDto>> ParseNonStandardShiftsCsvAsync(Stream csv, ActorContext actor, CancellationToken cancellationToken = default);
    Task<ConfigurationRevisionDto> CreateRevisionAsync(IReadOnlyList<RestIntervalDto> intervals, IReadOnlyList<NonStandardShiftDto> shifts, Guid? currentRevisionToken, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeDto>> ListAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default);
    Task<EmployeeDto> SaveAsync(SaveEmployeeCommand command, ActorContext actor, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<EmployeeImportPreviewDto> PreviewImportAsync(WorkspaceCode workspace, Stream csv, ActorContext actor, CancellationToken cancellationToken = default);
    Task ImportAsync(WorkspaceCode workspace, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IDemandService
{
    Task<IReadOnlyList<DateOnly>> ListMonthsAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default);
    Task<DemandDraftDto?> GetAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default);
    Task<DemandDraftDto> CreateAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<DemandDraftDto> UpdateEmployeeAsync(Guid demandId, string employeeCode, DateOnly? employmentStartDate, string? monthlyShift, int? openingRest, int? openingSpecialRest, int requestedLeaveRestCount, string? perpetualScheduleId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<DemandDraftDto> UpdateAssignmentAsync(Guid demandId, string employeeCode, DateOnly date, string? kind, bool requestedRest, string? station, string? shift, DateTimeOffset? eventStart, DateTimeOffset? eventEnd, string? eventDescription, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<ImportPreviewDto> PreviewDemandImportAsync(Guid demandId, Stream csv, ActorContext actor, CancellationToken cancellationToken = default);
    Task ImportDemandAsync(Guid demandId, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task UploadPreviousAsync(Guid demandId, string fileName, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task SelectPreviousScheduleAsync(Guid demandId, Guid scheduleVersionId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task UseUploadedPreviousScheduleAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<PreviousSchedulePreviewDto> GetPreviousSchedulePreviewAsync(Guid demandId, ActorContext actor, CancellationToken cancellationToken = default);
    Task<PreviousScheduleFileDto> ExportPreviousScheduleAsync(Guid demandId, ActorContext actor, CancellationToken cancellationToken = default);
    Task UploadPerpetualScheduleAsync(Guid demandId, string fileName, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task ClearPerpetualScheduleAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<PerpetualScheduleFileDto> ExportPerpetualScheduleAsync(Guid demandId, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IScheduleRunService
{
    Task<ScheduleRunDto> QueueAsync(Guid demandId, Guid revisionToken, ScheduleRunOptions options, ActorContext actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleRunDto>> ListAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleRunDto>> ListActiveAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleRunDto>> ListRecentAsync(int count, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IScheduleRunNotifier
{
    Task NotifyAsync(ScheduleRunDto run, CancellationToken cancellationToken = default);
}

public interface IScheduleService
{
    Task<IReadOnlyList<ScheduleMonthDto>> ListMonthsAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleVersionDto>> ListVersionsAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<ScheduleDetailDto> GetAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default);
    Task<ScheduleDetailDto> UpdateAssignmentAsync(Guid versionId, Guid assignmentId, string kind, bool requestedRest, string? station, string? shift, DateTimeOffset? eventStart, DateTimeOffset? eventEnd, string? eventDescription, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<ScheduleDetailDto> UpdateMonthlyShiftAsync(Guid versionId, Guid employeeSnapshotId, string monthlyShift, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task AdoptAsync(Guid versionId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid versionId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<byte[]> ExportCsvAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default);
    Task<byte[]> ExportExternalCsvAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default);
    Task<ScheduleVersionDto> ImportAsync(WorkspaceCode workspace, DateOnly month, string fileName, Stream csv, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IMPerpetualScheduleService
{
    Task<MPerpetualScheduleDto?> GetAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<MPerpetualScheduleDto> UploadAsync(string fileName, Stream csv, ActorContext actor, CancellationToken cancellationToken = default);
    Task<MPerpetualScheduleDto> SavePatternAsync(string? originalId, string id, IReadOnlyList<string> days, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<MPerpetualScheduleDto?> DeletePatternAsync(string id, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<PerpetualScheduleFileDto> ExportAsync(ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IScheduleValidationService
{
    Task<(IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<ScheduleEmployeeStats> Stats)> ValidateAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IAuditQueryService
{
    Task<IReadOnlyList<AuditLogDto>> QueryAsync(DateOnly? from, DateOnly? to, WorkspaceCode? workspace, string? action, ActorContext actor, CancellationToken cancellationToken = default);
}
