using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class DemandService(IDbContextFactory<NtmcDbContext> dbFactory) : IDemandService
{
    public async Task<IReadOnlyList<DateOnly>> ListMonthsAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        ServiceSupport.RequireViewer(actor);
        return await db.DemandDrafts.AsNoTracking().Where(x => x.Workspace == workspace).OrderByDescending(x => x.Month).Select(x => x.Month).ToArrayAsync(cancellationToken);
    }

    public async Task<DemandDraftDto?> GetAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        ServiceSupport.RequireViewer(actor);
        month = MonthStart(month);
        var demand = await Query(db).AsNoTracking().SingleOrDefaultAsync(x => x.Workspace == workspace && x.Month == month, cancellationToken);
        return demand is null ? null : ServiceSupport.ToDto(demand);
    }

    public async Task<DemandDraftDto> CreateAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        ServiceSupport.RequireEditor(actor, workspace);
        month = MonthStart(month);
        if (await db.DemandDrafts.AnyAsync(x => x.Workspace == workspace && x.Month == month, cancellationToken))
            throw new DomainValidationException("這個月份已經有需求資料。");
        var current = await db.CurrentConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken)
            ?? throw new DomainValidationException("請先建立八週區間與共同設定。");
        var employees = await db.Employees.AsNoTracking().Where(x => x.Workspace == workspace).OrderBy(x => x.EmployeeCode).ToListAsync(cancellationToken);
        if (employees.Count == 0) throw new DomainValidationException("請先建立員工資料。");
        var previousMonth = month.AddMonths(-1);
        var adopted = await db.AdoptedSchedules.AsNoTracking()
            .Include(x => x.ScheduleVersion).ThenInclude(x => x.Employees)
            .SingleOrDefaultAsync(x => x.Workspace == workspace && x.Month == previousMonth, cancellationToken);
        var demand = new DemandDraft
        {
            Workspace = workspace,
            Month = month,
            PreviousSource = adopted is null ? PreviousScheduleSource.Upload : PreviousScheduleSource.AdoptedSchedule,
            PreviousAdoptedScheduleVersionId = adopted?.ScheduleVersionId,
            ConfigurationRevisionId = current.ConfigurationRevisionId,
            CreatedByUserId = actor.UserId,
            UpdatedByUserId = actor.UserId
        };
        demand.Employees.AddRange(employees.Select(employee => new DemandEmployee
        {
            EmployeeCode = employee.EmployeeCode,
            Name = employee.Name,
            Affiliation = employee.Affiliation,
            EmploymentStartDate = employee.EmploymentStartDate,
            Ability = employee.Ability
        }));
        if (adopted is not null)
        {
            var previousByCode = adopted.ScheduleVersion.Employees.ToDictionary(x => x.EmployeeCode, StringComparer.Ordinal);
            foreach (var employee in demand.Employees)
            {
                if (!previousByCode.TryGetValue(employee.EmployeeCode, out var previous)) continue;
                employee.OpeningRest = previous.ClosingRest;
                employee.OpeningSpecialRest = previous.ClosingSpecialRest;
                employee.PerpetualScheduleId = workspace == WorkspaceCode.M ? previous.PerpetualScheduleId : null;
            }
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.DemandDrafts.Add(demand);
        ServiceSupport.AddAudit(db, actor, "DemandCreated", workspace, "DemandDraft", demand.Id, null,
            new { demand.Month, EmployeeCount = demand.Employees.Count, demand.PreviousSource, demand.ConfigurationRevisionId });
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceSupport.ToDto(demand);
    }

    public async Task DeleteAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await db.DemandDrafts.Include(x => x.UploadedPreviousSchedule).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var previous = demand.UploadedPreviousSchedule;
        ServiceSupport.AddAudit(db, actor, "DemandDeleted", demand.Workspace, "DemandDraft", demand.Id,
            new { demand.Month, EmployeeCount = await db.DemandEmployees.CountAsync(x => x.DemandDraftId == demand.Id, cancellationToken) }, null);
        db.DemandDrafts.Remove(demand);
        if (previous is not null) db.UploadedPreviousSchedules.Remove(previous);
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DemandDraftDto> UpdateEmployeeAsync(
        Guid demandId,
        string employeeCode,
        DateOnly? employmentStartDate,
        string? monthlyShift,
        int? openingRest,
        int? openingSpecialRest,
        int requestedLeaveRestCount,
        string? perpetualScheduleId,
        Guid revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var employee = await db.DemandEmployees.Include(x => x.DemandDraft)
            .SingleOrDefaultAsync(x => x.DemandDraftId == demandId && x.EmployeeCode == employeeCode, cancellationToken)
            ?? throw new DomainValidationException("找不到月份員工資料。");
        var demand = employee.DemandDraft;
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        if ((openingRest is null) != (openingSpecialRest is null) || openingRest < 0 || openingSpecialRest < 0 || requestedLeaveRestCount < 0)
            throw new DomainValidationException("月初 R/R1 必須同時填寫且不可為負數；R休上限不可為負數。");
        if (demand.Workspace == WorkspaceCode.T && SolverScheduleMapper.ParseShift(monthlyShift) is null)
            throw new DomainValidationException("T 月班別必須為早、午或夜。");
        if (demand.Workspace == WorkspaceCode.M && !string.IsNullOrWhiteSpace(monthlyShift))
            throw new DomainValidationException("M 不可設定 T 月班別。");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = new { employee.EmploymentStartDate, employee.MonthlyShift, employee.OpeningRest, employee.OpeningSpecialRest, employee.RequestedLeaveRestCount, employee.PerpetualScheduleId };
        employee.EmploymentStartDate = employmentStartDate;
        employee.MonthlyShift = string.IsNullOrWhiteSpace(monthlyShift) ? null : SolverScheduleMapper.ParseShift(monthlyShift).ToString();
        employee.OpeningRest = openingRest;
        employee.OpeningSpecialRest = openingSpecialRest;
        employee.RequestedLeaveRestCount = requestedLeaveRestCount;
        employee.PerpetualScheduleId = string.IsNullOrWhiteSpace(perpetualScheduleId) ? null : perpetualScheduleId.Trim();
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandEmployeeUpdated", demand.Workspace, "DemandEmployee", employee.Id, before,
            new { employee.EmploymentStartDate, employee.MonthlyShift, employee.OpeningRest, employee.OpeningSpecialRest, employee.RequestedLeaveRestCount, employee.PerpetualScheduleId });
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DemandDtoAsync(demand.Id, cancellationToken);
    }

    public async Task<ImportPreviewDto> PreviewDemandImportAsync(Guid demandId, Stream csv, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        ServiceSupport.RequireViewer(actor);
        var demand = await Query(db).AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        try
        {
            var schedule = await ParseMonthlyAsync(csv, demand, false, cancellationToken);
            ValidateWorkspace(schedule, demand.Workspace);
            var currentCodes = demand.Employees.Select(x => x.EmployeeCode).ToHashSet(StringComparer.Ordinal);
            var incomingCodes = schedule.Employees.Select(x => x.EmployeeId).ToHashSet(StringComparer.Ordinal);
            var differences = new List<string>();
            differences.AddRange(incomingCodes.Except(currentCodes).Order().Select(x => $"新增月份員工：{x}"));
            differences.AddRange(currentCodes.Except(incomingCodes).Order().Select(x => $"移除月份員工：{x}"));
            differences.Add($"匯入後共有 {schedule.Employees.Count} 位員工、{schedule.Employees.Sum(x => x.Assignments.Count)} 個固定日格。");
            return new(true, [], differences);
        }
        catch (Exception exception) when (exception is ScheduleCsvException or DomainValidationException)
        {
            return new(false, [exception.Message], []);
        }
    }

    public async Task<DemandDraftDto> UpdateAssignmentAsync(
        Guid demandId,
        string employeeCode,
        DateOnly date,
        string? kind,
        bool requestedRest,
        string? station,
        string? shift,
        DateTimeOffset? eventStart,
        DateTimeOffset? eventEnd,
        string? eventDescription,
        Guid revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var employee = await db.DemandEmployees.Include(x => x.Assignments).Include(x => x.DemandDraft)
            .SingleOrDefaultAsync(x => x.DemandDraftId == demandId && x.EmployeeCode == employeeCode, cancellationToken)
            ?? throw new DomainValidationException("找不到月份員工資料。");
        var demand = employee.DemandDraft;
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        if (date < demand.Month || date >= demand.Month.AddMonths(1)) throw new DomainValidationException("日格日期不在目前月份內。");
        ValidateDemandCell(demand.Workspace, date, kind, requestedRest, station, shift, eventStart, eventEnd, eventDescription);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var assignment = employee.Assignments.SingleOrDefault(x => x.Date == date);
        var before = assignment is null ? null : new { assignment.Kind, assignment.RequestedRest, assignment.Station, assignment.Shift, assignment.EventStart, assignment.EventEnd, assignment.EventDescription };
        if (string.IsNullOrWhiteSpace(kind) && !requestedRest)
        {
            if (assignment is not null) db.DemandAssignments.Remove(assignment);
        }
        else
        {
            if (assignment is null)
            {
                assignment = new DemandAssignment { DemandEmployeeId = employee.Id, Date = date };
                db.DemandAssignments.Add(assignment);
            }
            assignment.Kind = string.IsNullOrWhiteSpace(kind) || kind == "Unresolved" ? null : kind;
            assignment.RequestedRest = requestedRest;
            assignment.Station = kind == "Work" && demand.Workspace == WorkspaceCode.M ? station : null;
            assignment.Shift = kind == "Work" ? SolverScheduleMapper.ParseShift(shift).ToString() : null;
            assignment.EventStart = kind == "WorkEvent" ? eventStart : null;
            assignment.EventEnd = kind == "WorkEvent" ? eventEnd : null;
            assignment.EventDescription = kind == "WorkEvent" && !string.IsNullOrWhiteSpace(eventDescription) ? eventDescription.Trim() : null;
        }
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandAssignmentUpdated", demand.Workspace, "DemandEmployee", employee.Id, before,
            new { date, kind, requestedRest, station, shift, eventStart, eventEnd, eventDescription });
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DemandDtoAsync(demand.Id, cancellationToken);
    }

    public async Task ImportDemandAsync(Guid demandId, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var schedule = await ParseMonthlyAsync(csv, demand, false, cancellationToken);
        ValidateWorkspace(schedule, demand.Workspace);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = new { Employees = demand.Employees.Count, Assignments = demand.Employees.Sum(x => x.Assignments.Count) };
        db.DemandEmployees.RemoveRange(demand.Employees);
        demand.Employees = schedule.Employees.Select(SolverScheduleMapper.ToDemandEmployee).ToList();
        SetDemandRelationships(demand);
        db.DemandEmployees.AddRange(demand.Employees);
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandCsvImported", demand.Workspace, "DemandDraft", demand.Id, before,
            new { Employees = demand.Employees.Count, Assignments = demand.Employees.Sum(x => x.Assignments.Count) });
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UploadPreviousAsync(Guid demandId, string fileName, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var schedule = await ParseMonthlyAsync(csv, demand, true, cancellationToken);
        ValidateWorkspace(schedule, demand.Workspace);
        var upload = new UploadedPreviousSchedule
        {
            Workspace = demand.Workspace,
            Month = demand.Month.AddMonths(-1),
            FileName = Path.GetFileName(fileName),
            ParsedScheduleJson = JsonSerializer.Serialize(schedule, ServiceSupport.JsonOptions)
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var previousUpload = demand.UploadedPreviousSchedule;
        db.UploadedPreviousSchedules.Add(upload);
        demand.UploadedPreviousSchedule = upload;
        if (demand.PreviousSource == PreviousScheduleSource.Upload || demand.PreviousAdoptedScheduleVersionId is null)
        {
            demand.PreviousSource = PreviousScheduleSource.Upload;
            demand.PreviousAdoptedScheduleVersionId = null;
            ApplyPreviousSchedule(demand, schedule);
        }
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "PreviousScheduleUploaded", demand.Workspace, "DemandDraft", demand.Id, null,
            new { upload.Id, upload.Month, upload.FileName, EmployeeCount = schedule.Employees.Count });
        if (previousUpload is not null) db.UploadedPreviousSchedules.Remove(previousUpload);
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SelectPreviousScheduleAsync(
        Guid demandId,
        Guid scheduleVersionId,
        Guid revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var version = await db.ScheduleVersions.AsNoTracking().Include(x => x.Employees)
            .SingleOrDefaultAsync(x => x.Id == scheduleVersionId, cancellationToken)
            ?? throw new DomainValidationException("找不到選取的上月班表。");
        if (version.Workspace != demand.Workspace || version.Month != demand.Month.AddMonths(-1) || version.IsArchived)
            throw new DomainValidationException("只能選擇同工作區、前一月份且尚未封存的班表。");
        var previousByCode = version.Employees.ToDictionary(x => x.EmployeeCode, StringComparer.Ordinal);
        foreach (var employee in demand.Employees)
        {
            if (previousByCode.TryGetValue(employee.EmployeeCode, out var previous))
            {
                employee.OpeningRest = previous.ClosingRest;
                employee.OpeningSpecialRest = previous.ClosingSpecialRest;
                employee.PerpetualScheduleId = demand.Workspace == WorkspaceCode.M ? previous.PerpetualScheduleId : null;
            }
            else
            {
                employee.OpeningRest = null;
                employee.OpeningSpecialRest = null;
                employee.PerpetualScheduleId = null;
            }
        }
        var before = new { demand.PreviousSource, demand.PreviousAdoptedScheduleVersionId };
        demand.PreviousSource = PreviousScheduleSource.AdoptedSchedule;
        demand.PreviousAdoptedScheduleVersionId = version.Id;
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "PreviousScheduleSelected", demand.Workspace, "DemandDraft", demand.Id, before,
            new { ScheduleVersionId = version.Id, version.Month, version.Name });
        await SaveDemandChangesAsync(db, cancellationToken);
    }

    public async Task UseUploadedPreviousScheduleAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        if (demand.UploadedPreviousSchedule is null) throw new DomainValidationException("尚未上傳上月班表。");
        var schedule = JsonSerializer.Deserialize<MonthlySchedule>(demand.UploadedPreviousSchedule.ParsedScheduleJson, ServiceSupport.JsonOptions)
            ?? throw new DomainValidationException("上月班表快照無法讀取。");
        var before = new { demand.PreviousSource, demand.PreviousAdoptedScheduleVersionId };
        ApplyPreviousSchedule(demand, schedule);
        demand.PreviousSource = PreviousScheduleSource.Upload;
        demand.PreviousAdoptedScheduleVersionId = null;
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "UploadedPreviousScheduleSelected", demand.Workspace, "DemandDraft", demand.Id, before, new { demand.UploadedPreviousSchedule.FileName });
        await SaveDemandChangesAsync(db, cancellationToken);
    }

    public async Task<PreviousSchedulePreviewDto> GetPreviousSchedulePreviewAsync(Guid demandId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        ServiceSupport.RequireViewer(actor);
        var demand = await Query(db).AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        var schedule = await GetPreviousScheduleAsync(db, demand, cancellationToken);
        return new(demand.Workspace, schedule.MonthStart, schedule.Employees.Select(employee =>
            new PreviousScheduleEmployeeDto(employee.EmployeeId, employee.Name, employee.Affiliation, ScheduleCsv.MonthlyRow(schedule, employee))).ToArray());
    }

    public async Task<PreviousScheduleFileDto> ExportPreviousScheduleAsync(Guid demandId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        ServiceSupport.RequireViewer(actor);
        var demand = await Query(db).AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        var schedule = await GetPreviousScheduleAsync(db, demand, cancellationToken);
        var fileName = demand.PreviousSource == PreviousScheduleSource.Upload
            ? demand.UploadedPreviousSchedule?.FileName ?? $"previous-{schedule.MonthStart:yyyy-MM}.csv"
            : $"previous-{schedule.MonthStart:yyyy-MM}.csv";
        return new(fileName, ScheduleCsv.WriteMonthly(schedule));
    }

    public async Task UploadPerpetualScheduleAsync(Guid demandId, string fileName, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.Workspace != WorkspaceCode.M) throw new DomainValidationException("只有 M 可上傳八週萬年班表。");
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var schedule = await UploadFile.ParseAsync(csv, ScheduleCsv.ReadMPerpetualSchedule, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        demand.PerpetualScheduleJson = JsonSerializer.Serialize(schedule, ServiceSupport.JsonOptions);
        demand.PerpetualScheduleFileName = Path.GetFileName(fileName);
        demand.PerpetualScheduleUploadedAtUtc = DateTimeOffset.UtcNow;
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "PerpetualScheduleUploaded", demand.Workspace, "DemandDraft", demand.Id, null,
            new { PatternCount = schedule.Patterns.Count });
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PerpetualScheduleFileDto> ExportPerpetualScheduleAsync(Guid demandId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        ServiceSupport.RequireViewer(actor);
        var demand = await db.DemandDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        if (demand.Workspace != WorkspaceCode.M || string.IsNullOrWhiteSpace(demand.PerpetualScheduleJson))
            throw new DomainValidationException("找不到 M 八週萬年班表。");
        var schedule = JsonSerializer.Deserialize<MPerpetualSchedule>(demand.PerpetualScheduleJson, ServiceSupport.JsonOptions)
            ?? throw new DomainValidationException("M 八週萬年班表無法讀取。");
        return new(demand.PerpetualScheduleFileName ?? "perpetual.csv", ScheduleCsv.WriteMPerpetualSchedule(schedule));
    }

    public async Task ClearPerpetualScheduleAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await db.DemandDrafts.SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.Workspace != WorkspaceCode.M) throw new DomainValidationException("只有 M 使用萬年班表。");
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var before = new { demand.PerpetualScheduleFileName, demand.PerpetualScheduleUploadedAtUtc };
        demand.PerpetualScheduleJson = null;
        demand.PerpetualScheduleFileName = null;
        demand.PerpetualScheduleUploadedAtUtc = null;
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandPerpetualScheduleCleared", WorkspaceCode.M, "DemandDraft", demand.Id, before, null);
        await SaveDemandChangesAsync(db, cancellationToken);
    }

    private static IQueryable<DemandDraft> Query(NtmcDbContext db) => db.DemandDrafts.AsSplitQuery()
        .Include(x => x.ConfigurationRevision).ThenInclude(x => x.NonStandardShifts)
        .Include(x => x.Employees).ThenInclude(x => x.Assignments)
        .Include(x => x.UploadedPreviousSchedule);

    private async Task<DemandDraftDto> DemandDtoAsync(Guid demandId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).AsNoTracking().SingleAsync(x => x.Id == demandId, cancellationToken);
        return ServiceSupport.ToDto(demand);
    }

    private async Task<MonthlySchedule> ParseMonthlyAsync(Stream csv, DemandDraft demand, bool historical, CancellationToken cancellationToken)
    {
        var shifts = SolverScheduleMapper.ToNonStandardShifts(demand.ConfigurationRevision);
        var month = historical ? demand.Month.AddMonths(-1) : demand.Month;
        return await UploadFile.ParseAsync(csv, path => ScheduleCsv.ReadMonthly(path, month, shifts, historical), cancellationToken);
    }

    private static async Task<MonthlySchedule> GetPreviousScheduleAsync(NtmcDbContext db, DemandDraft demand, CancellationToken cancellationToken)
    {
        if (demand.PreviousSource == PreviousScheduleSource.Upload)
            return demand.UploadedPreviousSchedule is null
                ? throw new DomainValidationException("請先上傳上月班表。")
                : JsonSerializer.Deserialize<MonthlySchedule>(demand.UploadedPreviousSchedule.ParsedScheduleJson, ServiceSupport.JsonOptions)
                    ?? throw new DomainValidationException("上月班表快照無法讀取。");

        if (demand.PreviousAdoptedScheduleVersionId is not { } versionId)
            throw new DomainValidationException("找不到選取的上月班表。");
        var version = await db.ScheduleVersions.AsNoTracking().Include(x => x.Employees).ThenInclude(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == versionId && !x.IsArchived, cancellationToken)
            ?? throw new DomainValidationException("選取的上月班表不存在或已封存。");
        return SolverScheduleMapper.ToMonthlySchedule(version);
    }

    private static void ValidateWorkspace(MonthlySchedule schedule, WorkspaceCode workspace)
    {
        var isT = schedule.Employees.Any(x => x.Ability is not null || x.MonthlyShift is not null);
        if (isT != (workspace == WorkspaceCode.T)) throw new DomainValidationException("CSV 的 M/T 欄位與目前工作區不符。");
    }

    private static void ApplyPreviousSchedule(DemandDraft demand, MonthlySchedule schedule)
    {
        var previousByCode = schedule.Employees.ToDictionary(x => x.EmployeeId, StringComparer.Ordinal);
        foreach (var employee in demand.Employees)
        {
            if (previousByCode.TryGetValue(employee.EmployeeCode, out var previous))
            {
                employee.OpeningRest = previous.ClosingUsage?.Rest;
                employee.OpeningSpecialRest = previous.ClosingUsage?.SpecialRest;
                employee.PerpetualScheduleId = demand.Workspace == WorkspaceCode.M ? previous.PerpetualScheduleId : null;
            }
            else
            {
                employee.OpeningRest = null;
                employee.OpeningSpecialRest = null;
                employee.PerpetualScheduleId = null;
            }
        }
    }

    private static void SetDemandRelationships(DemandDraft demand)
    {
        foreach (var employee in demand.Employees)
        {
            employee.DemandDraftId = demand.Id;
            foreach (var assignment in employee.Assignments)
            {
                assignment.DemandEmployeeId = employee.Id;
            }
        }
    }

    private static DateOnly MonthStart(DateOnly month) => new(month.Year, month.Month, 1);

    private static void ValidateDemandCell(WorkspaceCode workspace, DateOnly date, string? kind, bool requestedRest, string? station, string? shift, DateTimeOffset? eventStart, DateTimeOffset? eventEnd, string? description)
    {
        if (kind is not (null or "" or "Unresolved" or "Work" or "Rest" or "SpecialRest" or "LeaveRest" or "WorkEvent"))
            throw new DomainValidationException("不支援的需求日格狀態。");
        if (kind == "LeaveRest" && !requestedRest) throw new DomainValidationException("需求中的 R休 必須來自 R*。");
        if (requestedRest && kind is not (null or "" or "Unresolved" or "Rest" or "SpecialRest" or "LeaveRest"))
            throw new DomainValidationException("R* 標記只能套用在未決定或休假日格。");
        if (kind == "Work")
        {
            if (SolverScheduleMapper.ParseShift(shift) is null) throw new DomainValidationException("正常班必須指定班別。");
            if (workspace == WorkspaceCode.M && !IsMStation(station)) throw new DomainValidationException("M 正常班車站必須為 LB01–LB12。");
            if (workspace == WorkspaceCode.T && !string.IsNullOrWhiteSpace(station)) throw new DomainValidationException("T 正常班不可指定車站。");
        }
        if (kind == "WorkEvent" && (eventStart is null || eventEnd is null || eventEnd <= eventStart || eventEnd - eventStart > TimeSpan.FromHours(24) || eventStart.Value.Offset != TimeSpan.FromHours(8) || eventEnd.Value.Offset != TimeSpan.FromHours(8)))
            throw new DomainValidationException("X 必須使用台北時間，結束晚於開始且長度不超過 24 小時。");
        if (kind == "WorkEvent" && DateOnly.FromDateTime(eventStart!.Value.DateTime) != date)
            throw new DomainValidationException("X 必須歸在台北時間的開始日期。");
        if (description?.Length > 500) throw new DomainValidationException("X 說明不可超過 500 字元。");
    }
    private static bool IsMStation(string? station) => station is not null && station.Length == 4 &&
        station.StartsWith("LB", StringComparison.Ordinal) && int.TryParse(station[2..], out var number) && number is >= 1 and <= 12;

    private static async Task SaveDemandChangesAsync(NtmcDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("本月需求資料已失效，請重新整理後再儲存。");
        }
    }
    private static void Touch(DemandDraft demand, Guid actorId)
    {
        demand.UpdatedByUserId = actorId;
        demand.UpdatedAtUtc = DateTimeOffset.UtcNow;
        demand.RevisionToken = Guid.NewGuid();
    }
}
