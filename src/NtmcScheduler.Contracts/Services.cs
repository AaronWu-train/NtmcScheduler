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
    Task<DemandDraftDto?> GetAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default);
    Task<DemandDraftDto> CreateAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default);
    Task<DemandDraftDto> UpdateEmployeeAsync(Guid demandEmployeeId, string? monthlyShift, int? openingRest, int? openingSpecialRest, int requestedLeaveRestCount, string? perpetualScheduleId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<DemandDraftDto> UpdateAssignmentAsync(Guid demandEmployeeId, DateOnly date, string? kind, bool requestedRest, string? station, string? shift, DateTimeOffset? eventStart, DateTimeOffset? eventEnd, string? eventDescription, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<ImportPreviewDto> PreviewDemandImportAsync(Guid demandId, Stream csv, ActorContext actor, CancellationToken cancellationToken = default);
    Task ImportDemandAsync(Guid demandId, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task UploadPreviousAsync(Guid demandId, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task UploadPerpetualScheduleAsync(Guid demandId, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IScheduleRunService
{
    Task<ScheduleRunDto> QueueAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleRunDto>> ListAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IScheduleService
{
    Task<IReadOnlyList<ScheduleMonthDto>> ListMonthsAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleVersionDto>> ListVersionsAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<ScheduleDetailDto> GetAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default);
    Task<ScheduleDetailDto> UpdateAssignmentAsync(Guid versionId, Guid assignmentId, string kind, bool requestedRest, string? station, string? shift, DateTimeOffset? eventStart, DateTimeOffset? eventEnd, string? eventDescription, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task AdoptAsync(Guid versionId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid versionId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default);
    Task<byte[]> ExportCsvAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default);
    Task<byte[]> ExportExternalCsvAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IScheduleValidationService
{
    Task<(IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<ScheduleEmployeeStats> Stats)> ValidateAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default);
}

public interface IAuditQueryService
{
    Task<IReadOnlyList<AuditLogDto>> QueryAsync(DateOnly? from, DateOnly? to, WorkspaceCode? workspace, string? action, ActorContext actor, CancellationToken cancellationToken = default);
}
