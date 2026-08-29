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
        ServiceSupport.RequireEditor(actor, workspace);
        return await db.DemandDrafts.AsNoTracking().Where(x => x.Workspace == workspace).OrderByDescending(x => x.Month).Select(x => x.Month).ToArrayAsync(cancellationToken);
    }

    public async Task<DemandDraftDto?> GetAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        ServiceSupport.RequireEditor(actor, workspace);
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
            RequestedRestLimit = 4,
            CreatedByUserId = actor.UserId,
            UpdatedByUserId = actor.UserId
        };
        if (workspace.IsStation())
            demand.MStationSettingsJson = await db.DemandDrafts.AsNoTracking()
                .Where(x => x.Workspace == workspace && x.Month == previousMonth)
                .Select(x => x.MStationSettingsJson).SingleOrDefaultAsync(cancellationToken);
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
                employee.PerpetualScheduleId = workspace.IsStation() ? previous.PerpetualScheduleId : null;
            }
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.DemandDrafts.Add(demand);
        ServiceSupport.AddAudit(db, actor, "DemandCreated", workspace, "DemandDraft", demand.Id, null,
            new { demand.Month, EmployeeCount = demand.Employees.Count, demand.PreviousSource, demand.ConfigurationRevisionId });
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DemandDtoAsync(demand.Id, cancellationToken);
    }

    public async Task<DemandDraftDto> UpdateMonthlySettingsAsync(
        Guid demandId,
        int generalRestTarget,
        int specialRestTarget,
        int requestedRestLimit,
        IReadOnlyList<MStationSettingDto> stations,
        Guid revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        if (requestedRestLimit < 0) throw new DomainValidationException("每人 R* 上限必須是非負整數。");
        var bounds = SolverScheduleMapper.ToDto(demand);
        if (generalRestTarget < bounds.GeneralRestMinimum || generalRestTarget > bounds.GeneralRestMaximum ||
            specialRestTarget < bounds.SpecialRestMinimum || specialRestTarget > bounds.SpecialRestMaximum)
            throw new DomainValidationException($"R 目標必須介於 {bounds.GeneralRestMinimum}–{bounds.GeneralRestMaximum}，R1 目標必須介於 {bounds.SpecialRestMinimum}–{bounds.SpecialRestMaximum}。");

        MStationSetting[] mapped = [];
        if (demand.Workspace.IsStation())
        {
            var expectedCodes = demand.Workspace.Stations();
            if (stations.Count != expectedCodes.Count || !stations.Select(x => x.Code).SequenceEqual(expectedCodes, StringComparer.Ordinal))
                throw new DomainValidationException($"{demand.Workspace.DisplayName()} 車站固定為 {expectedCodes[0]}–{expectedCodes[expectedCodes.Count - 1]}，且順序不可變更。");
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            mapped = stations.Select(row =>
            {
                var code = row.Code.Trim();
                var group = row.Group.Trim();
                if (!codes.Add(code)) throw new DomainValidationException("站務車站代碼不可重複。");
                if (string.IsNullOrWhiteSpace(group) || group.Length > 64) throw new DomainValidationException($"車站 {code} 的群組必填且最多 64 字。");
                static StaffingRange Range(StaffingRangeDto value, string label)
                {
                    if (value.Minimum < 0 || value.Maximum < value.Minimum) throw new DomainValidationException($"{label} 人數必須符合 0 ≤ 最少 ≤ 最多。");
                    return new(value.Minimum, value.Maximum);
                }
                return new MStationSetting(code, group, (ExternalSupportLevel)row.ExternalSupport,
                    Range(row.Early, $"{code} 早班"), Range(row.Afternoon, $"{code} 小班"), Range(row.Night, $"{code} 夜班"));
            }).ToArray();
        }

        var before = new { demand.GeneralRestTarget, demand.SpecialRestTarget, demand.RequestedRestLimit, demand.MStationSettingsJson };
        demand.GeneralRestTarget = generalRestTarget;
        demand.SpecialRestTarget = specialRestTarget;
        demand.RequestedRestLimit = requestedRestLimit;
        demand.MStationSettingsJson = demand.Workspace.IsStation() ? JsonSerializer.Serialize(mapped, ServiceSupport.JsonOptions) : null;
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandMonthlySettingsUpdated", demand.Workspace, "DemandDraft", demand.Id, before,
            new { demand.Month, demand.GeneralRestTarget, demand.SpecialRestTarget, demand.RequestedRestLimit, Stations = mapped.Length });
        await SaveDemandChangesAsync(db, cancellationToken);
        return await DemandDtoAsync(demand.Id, cancellationToken);
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
        DateOnly? employmentEndDate,
        string? monthlyShift,
        int? openingRest,
        int? openingSpecialRest,
        int requestedLeaveRestMinimum,
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
        if ((openingRest is null) != (openingSpecialRest is null) || openingRest < 0 || openingSpecialRest < 0 ||
            requestedLeaveRestMinimum < 0 || requestedLeaveRestCount < 0 || requestedLeaveRestMinimum > requestedLeaveRestCount)
            throw new DomainValidationException("月初 R/R1 必須同時填寫且不可為負數；R休上下界必須符合 0 ≤ 下界 ≤ 上界。");
        if (employmentStartDate is { } start && employmentEndDate is { } end && start > end)
            throw new DomainValidationException("月中排班終止日不得早於月中開始排班日。");
        if (demand.Workspace.IsMaintenance() && SolverScheduleMapper.ParseShift(monthlyShift) is null)
            throw new DomainValidationException("T 月班別必須為早、午或夜。");
        if (demand.Workspace.IsStation() && !string.IsNullOrWhiteSpace(monthlyShift))
            throw new DomainValidationException("站務工作區不可設定 T 月班別。");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = new { demand.Month, employee.EmployeeCode, employee.Name, employee.EmploymentStartDate, employee.EmploymentEndDate, employee.MonthlyShift, employee.OpeningRest, employee.OpeningSpecialRest, employee.RequestedLeaveRestMinimum, employee.RequestedLeaveRestCount, employee.PerpetualScheduleId };
        employee.EmploymentStartDate = employmentStartDate;
        employee.EmploymentEndDate = employmentEndDate;
        employee.MonthlyShift = string.IsNullOrWhiteSpace(monthlyShift) ? null : SolverScheduleMapper.ParseShift(monthlyShift).ToString();
        employee.OpeningRest = openingRest;
        employee.OpeningSpecialRest = openingSpecialRest;
        employee.RequestedLeaveRestMinimum = requestedLeaveRestMinimum;
        employee.RequestedLeaveRestCount = requestedLeaveRestCount;
        employee.PerpetualScheduleId = string.IsNullOrWhiteSpace(perpetualScheduleId) ? null : perpetualScheduleId.Trim();
        await db.DemandAssignments.Where(x => x.DemandEmployeeId == employee.Id &&
            (employmentStartDate != null && x.Date < employmentStartDate || employmentEndDate != null && x.Date > employmentEndDate))
            .ExecuteDeleteAsync(cancellationToken);
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandEmployeeUpdated", demand.Workspace, "DemandEmployee", employee.Id, before,
            new { demand.Month, employee.EmployeeCode, employee.Name, employee.EmploymentStartDate, employee.EmploymentEndDate, employee.MonthlyShift, employee.OpeningRest, employee.OpeningSpecialRest, employee.RequestedLeaveRestMinimum, employee.RequestedLeaveRestCount, employee.PerpetualScheduleId });
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DemandDtoAsync(demand.Id, cancellationToken);
    }

    public async Task<ImportPreviewDto> PreviewDemandImportAsync(Guid demandId, Stream csv, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        try
        {
            var schedule = await ParseMonthlyAsync(db, csv, demand, false, cancellationToken);
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
        if (employee.EmploymentStartDate is { } employmentStart && date < employmentStart || employee.EmploymentEndDate is { } employmentEnd && date > employmentEnd)
            throw new DomainValidationException("日格日期不在此員工的月間排班範圍內。");
        var mStations = demand.Workspace.IsStation()
            ? (string.IsNullOrWhiteSpace(demand.MStationSettingsJson)
                ? demand.Workspace.Stations().ToHashSet(StringComparer.Ordinal)
                : (JsonSerializer.Deserialize<MStationSetting[]>(demand.MStationSettingsJson, ServiceSupport.JsonOptions) ?? []).Select(x => x.Code).ToHashSet(StringComparer.Ordinal))
            : null;
        DemandCellValidator.Validate(demand.Workspace, date, kind, requestedRest, station, shift, eventStart, eventEnd, eventDescription, mStations);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var assignment = employee.Assignments.SingleOrDefault(x => x.Date == date);
        var before = assignment is null ? null : new { demand.Month, employee.EmployeeCode, employee.Name, assignment.Date, assignment.Kind, assignment.RequestedRest, assignment.Station, assignment.Shift, assignment.EventStart, assignment.EventEnd, assignment.EventDescription };
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
            assignment.Station = kind == "Work" && demand.Workspace.IsStation() ? station : null;
            assignment.Shift = kind == "Work" ? SolverScheduleMapper.ParseShift(shift).ToString() : null;
            assignment.EventStart = kind == "WorkEvent" ? eventStart : null;
            assignment.EventEnd = kind == "WorkEvent" ? eventEnd : null;
            assignment.EventDescription = kind == "WorkEvent" && !string.IsNullOrWhiteSpace(eventDescription) ? eventDescription.Trim() : null;
        }
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandAssignmentUpdated", demand.Workspace, "DemandEmployee", employee.Id, before,
            new { demand.Month, employee.EmployeeCode, employee.Name, date, kind, requestedRest, station, shift, eventStart, eventEnd, eventDescription });
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
        var schedule = await ParseMonthlyAsync(db, csv, demand, false, cancellationToken);
        ValidateWorkspace(schedule, demand.Workspace);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = new { demand.Month, Employees = demand.Employees.Count, Assignments = demand.Employees.Sum(x => x.Assignments.Count) };
        db.DemandEmployees.RemoveRange(demand.Employees);
        demand.Employees = schedule.Employees.Select(SolverScheduleMapper.ToDemandEmployee).ToList();
        SetDemandRelationships(demand);
        InheritBlankPerpetualScheduleIds(demand, await TryGetPreviousScheduleAsync(db, demand, cancellationToken));
        db.DemandEmployees.AddRange(demand.Employees);
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandCsvImported", demand.Workspace, "DemandDraft", demand.Id, before,
            new { demand.Month, Employees = demand.Employees.Count, Assignments = demand.Employees.Sum(x => x.Assignments.Count) });
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
        var schedule = await ParseMonthlyAsync(db, csv, demand, true, cancellationToken);
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
            new { DemandMonth = demand.Month, upload.Id, upload.Month, upload.FileName, EmployeeCount = schedule.Employees.Count });
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
                employee.PerpetualScheduleId = demand.Workspace.IsStation() ? previous.PerpetualScheduleId : null;
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
        ServiceSupport.AddAudit(db, actor, "UploadedPreviousScheduleSelected", demand.Workspace, "DemandDraft", demand.Id, before,
            new { demand.Month, demand.UploadedPreviousSchedule!.FileName });
        await SaveDemandChangesAsync(db, cancellationToken);
    }

    public async Task<DemandDraftDto> RestorePreviousInheritedFieldsAsync(
        Guid demandId,
        Guid revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var previous = await TryGetPreviousScheduleAsync(db, demand, cancellationToken)
            ?? throw new DomainValidationException("找不到上月班表來源，請先在步驟一設定上月班表。");
        var before = demand.Employees.Select(employee => new
        {
            employee.EmployeeCode,
            employee.OpeningRest,
            employee.OpeningSpecialRest,
            employee.PerpetualScheduleId
        }).ToArray();
        ApplyPreviousSchedule(demand, previous);
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandPreviousInheritedFieldsRestored", demand.Workspace, "DemandDraft", demand.Id, before,
            new { demand.Month, demand.PreviousSource, EmployeeCount = demand.Employees.Count });
        await SaveDemandChangesAsync(db, cancellationToken);
        return await DemandDtoAsync(demand.Id, cancellationToken);
    }

    public async Task<PreviousSchedulePreviewDto> GetPreviousSchedulePreviewAsync(Guid demandId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        var schedule = await GetPreviousScheduleAsync(db, demand, cancellationToken);
        return new(demand.Workspace, schedule.MonthStart, schedule.Employees.Select(employee =>
            new PreviousScheduleEmployeeDto(employee.EmployeeId, employee.Name, employee.Affiliation,
                employee.ClosingUsage?.Rest, employee.ClosingUsage?.SpecialRest, ScheduleCsv.MonthlyRow(schedule, employee))).ToArray());
    }

    public async Task<PreviousScheduleFileDto> ExportPreviousScheduleAsync(Guid demandId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        var schedule = await GetPreviousScheduleAsync(db, demand, cancellationToken);
        var fileName = demand.PreviousSource == PreviousScheduleSource.Upload
            ? demand.UploadedPreviousSchedule?.FileName ?? $"previous-{schedule.MonthStart:yyyy-MM}.csv"
            : $"previous-{schedule.MonthStart:yyyy-MM}.csv";
        return new(fileName, ScheduleCsv.WriteMonthlyDownload(schedule, demand.Workspace));
    }

    public async Task UploadPerpetualScheduleAsync(Guid demandId, string fileName, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (!demand.Workspace.IsStation()) throw new DomainValidationException("只有站務工作區可上傳八週萬年班表。");
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var schedule = await UploadFile.ParseAsync(csv, path => ScheduleCsv.ReadMPerpetualSchedule(path, demand.Workspace), cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        demand.PerpetualScheduleJson = JsonSerializer.Serialize(schedule, ServiceSupport.JsonOptions);
        demand.PerpetualScheduleFileName = Path.GetFileName(fileName);
        demand.PerpetualScheduleUploadedAtUtc = DateTimeOffset.UtcNow;
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "PerpetualScheduleUploaded", demand.Workspace, "DemandDraft", demand.Id, null,
            new { demand.Month, FileName = demand.PerpetualScheduleFileName, PatternCount = schedule.Patterns.Count });
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PerpetualScheduleFileDto> ExportPerpetualScheduleAsync(Guid demandId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await db.DemandDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (!demand.Workspace.IsStation() || string.IsNullOrWhiteSpace(demand.PerpetualScheduleJson))
            throw new DomainValidationException("找不到站務八週萬年班表。");
        var schedule = JsonSerializer.Deserialize<MPerpetualSchedule>(demand.PerpetualScheduleJson, ServiceSupport.JsonOptions)
            ?? throw new DomainValidationException("站務八週萬年班表無法讀取。");
        return new(demand.PerpetualScheduleFileName ?? "perpetual.csv", ScheduleCsv.WriteMPerpetualSchedule(schedule, demand.Workspace));
    }

    public async Task ClearPerpetualScheduleAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await db.DemandDrafts.SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (!demand.Workspace.IsStation()) throw new DomainValidationException("只有站務工作區使用萬年班表。");
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var before = new { demand.Month, demand.PerpetualScheduleFileName, demand.PerpetualScheduleUploadedAtUtc };
        demand.PerpetualScheduleJson = null;
        demand.PerpetualScheduleFileName = null;
        demand.PerpetualScheduleUploadedAtUtc = null;
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandPerpetualScheduleCleared", demand.Workspace, "DemandDraft", demand.Id, before, null);
        await SaveDemandChangesAsync(db, cancellationToken);
    }

    public async Task UseEmptyPerpetualScheduleAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await db.DemandDrafts.SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (!demand.Workspace.IsStation()) throw new DomainValidationException("只有站務工作區使用萬年班表。");
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var before = new { demand.Month, demand.PerpetualScheduleFileName, demand.PerpetualScheduleUploadedAtUtc };
        demand.PerpetualScheduleJson = JsonSerializer.Serialize(new MPerpetualSchedule(new Dictionary<string, IReadOnlyList<ScheduleCell?>>()), ServiceSupport.JsonOptions);
        demand.PerpetualScheduleFileName = null;
        demand.PerpetualScheduleUploadedAtUtc = DateTimeOffset.UtcNow;
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandEmptyPerpetualScheduleSelected", demand.Workspace, "DemandDraft", demand.Id, before, new { demand.Month });
        await SaveDemandChangesAsync(db, cancellationToken);
    }

    public async Task<DemandDraftDto> ImportEmployeeSubmissionsAsync(Guid demandId, IReadOnlyCollection<string> employeeCodes, Guid revisionToken,
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await db.DemandDrafts
            .Include(x => x.Employees)
            .SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理。");
        var selectedCodes = employeeCodes.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);
        if (selectedCodes.Count == 0) throw new DomainValidationException("至少選擇一位要匯入的員工。");
        var demandByCode = demand.Employees.ToDictionary(x => x.EmployeeCode, StringComparer.Ordinal);
        if (selectedCodes.Any(code => !demandByCode.ContainsKey(code)))
            throw new DomainValidationException("選取的員工不在本月 Demand。");
        var submissions = await db.EmployeeDemandSubmissions.AsNoTracking()
            .Include(x => x.Assignments)
            .Where(x => x.Workspace == demand.Workspace && x.Month == demand.Month && selectedCodes.Contains(x.EmployeeCode))
            .ToListAsync(cancellationToken);
        if (submissions.Count != selectedCodes.Count) throw new DomainValidationException("選取的員工填報不存在或不屬於本月。");

        var importedEmployees = 0;
        var importedAssignments = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var submission in submissions)
        {
            if (!demandByCode.TryGetValue(submission.EmployeeCode, out var demandEmployee)) continue;
            if (submission.RequestedLeaveRestMinimum < 0 || submission.RequestedLeaveRestCount < 0 || submission.RequestedLeaveRestMinimum > submission.RequestedLeaveRestCount)
                throw new DomainValidationException($"{submission.EmployeeCode} 的 R休上下界不合法。");
            demandEmployee.RequestedLeaveRestMinimum = submission.RequestedLeaveRestMinimum;
            demandEmployee.RequestedLeaveRestCount = submission.RequestedLeaveRestCount;
            await db.DemandAssignments.Where(x => x.DemandEmployeeId == demandEmployee.Id).ExecuteDeleteAsync(cancellationToken);
            foreach (var source in submission.Assignments)
            {
                db.DemandAssignments.Add(new DemandAssignment
                {
                    DemandEmployeeId = demandEmployee.Id,
                    Date = source.Date,
                    Kind = source.Kind,
                    RequestedRest = source.RequestedRest,
                    Station = source.Station,
                    Shift = source.Shift,
                    EventStart = source.EventStart,
                    EventEnd = source.EventEnd,
                    EventDescription = source.EventDescription
                });
                importedAssignments++;
            }
            importedEmployees++;
        }

        var existingImport = await db.DemandSubmissionImports.SingleOrDefaultAsync(x => x.DemandDraftId == demandId, cancellationToken);
        var importedAt = DateTimeOffset.UtcNow;
        if (existingImport is null)
        {
            db.DemandSubmissionImports.Add(new DemandSubmissionImport
            {
                DemandDraftId = demand.Id,
                ImportedAtUtc = importedAt,
                ImportedByUserId = actor.UserId,
                ImportedByName = actor.UserName
            });
        }
        else
        {
            existingImport.ImportedAtUtc = importedAt;
            existingImport.ImportedByUserId = actor.UserId;
            existingImport.ImportedByName = actor.UserName;
        }
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandSubmissionImported", demand.Workspace, "DemandDraft", demand.Id, null,
            new { demand.Month, EmployeeCodes = selectedCodes.Order().ToArray(), ImportedEmployees = importedEmployees, ImportedAssignments = importedAssignments });
        await SaveDemandChangesAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DemandDtoAsync(demand.Id, cancellationToken);
    }

    private static IQueryable<DemandDraft> Query(NtmcDbContext db) => db.DemandDrafts.AsSplitQuery()
        .Include(x => x.ConfigurationRevision).ThenInclude(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
        .Include(x => x.ConfigurationRevision).ThenInclude(x => x.NonStandardShifts)
        .Include(x => x.Employees).ThenInclude(x => x.Assignments)
        .Include(x => x.UploadedPreviousSchedule);

    private async Task<DemandDraftDto> DemandDtoAsync(Guid demandId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await Query(db).AsNoTracking().SingleAsync(x => x.Id == demandId, cancellationToken);
        return ServiceSupport.ToDto(demand);
    }

    private static async Task<MonthlySchedule> ParseMonthlyAsync(NtmcDbContext db, Stream csv, DemandDraft demand, bool historical, CancellationToken cancellationToken)
    {
        var shifts = SolverScheduleMapper.ToNonStandardShifts(demand.ConfigurationRevision);
        var month = historical ? demand.Month.AddMonths(-1) : demand.Month;
        var schedule = await UploadFile.ParseAsync(csv,
            path => ScheduleCsv.ReadMonthly(path, month, shifts, historical, demand.Workspace, ignoreDerivedHistoricalFields: historical), cancellationToken);
        if (!historical) return schedule;
        var abilities = await db.Employees.AsNoTracking().Where(x => x.Workspace == demand.Workspace)
            .ToDictionaryAsync(x => x.EmployeeCode, x => x.Ability, StringComparer.Ordinal, cancellationToken);
        var adopted = await db.AdoptedSchedules.AsNoTracking().Include(x => x.ScheduleVersion).ThenInclude(x => x.Employees)
            .SingleOrDefaultAsync(x => x.Workspace == demand.Workspace && x.Month == month.AddMonths(-1), cancellationToken);
        var adoptedClosing = adopted?.ScheduleVersion.Employees
            .Where(x => x.ClosingRest is not null && x.ClosingSpecialRest is not null)
            .ToDictionary(x => x.EmployeeCode, x => new RestUsage(x.ClosingRest!.Value, x.ClosingSpecialRest!.Value), StringComparer.Ordinal)
            ?? new Dictionary<string, RestUsage>(StringComparer.Ordinal);
        return SolverScheduleMapper.CompleteHistoricalImport(schedule, demand.Workspace, abilities, adoptedClosing,
            SolverScheduleMapper.ToRestIntervals(demand.ConfigurationRevision));
    }

    private static async Task<MonthlySchedule> GetPreviousScheduleAsync(NtmcDbContext db, DemandDraft demand, CancellationToken cancellationToken)
    {
        var previous = await TryGetPreviousScheduleAsync(db, demand, cancellationToken);
        if (previous is not null) return previous;
        throw new DomainValidationException(demand.PreviousSource == PreviousScheduleSource.Upload
            ? "請先上傳上月班表。"
            : demand.PreviousAdoptedScheduleVersionId is null
                ? "找不到選取的上月班表。"
                : "選取的上月班表不存在或已封存。");
    }

    private static async Task<MonthlySchedule?> TryGetPreviousScheduleAsync(NtmcDbContext db, DemandDraft demand, CancellationToken cancellationToken)
    {
        if (demand.PreviousSource == PreviousScheduleSource.Upload)
        {
            if (demand.UploadedPreviousSchedule is null) return null;
            return JsonSerializer.Deserialize<MonthlySchedule>(demand.UploadedPreviousSchedule.ParsedScheduleJson, ServiceSupport.JsonOptions)
                ?? throw new DomainValidationException("上月班表快照無法讀取。");
        }
        if (demand.PreviousAdoptedScheduleVersionId is not { } versionId) return null;
        var version = await db.ScheduleVersions.AsNoTracking().Include(x => x.Employees).ThenInclude(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == versionId && !x.IsArchived, cancellationToken);
        return version is null ? null : SolverScheduleMapper.ToMonthlySchedule(version);
    }

    private static void ValidateWorkspace(MonthlySchedule schedule, WorkspaceCode workspace)
    {
        var isT = schedule.Employees.Any(x => x.Ability is not null || x.MonthlyShift is not null);
        if (isT != workspace.IsMaintenance()) throw new DomainValidationException("CSV 的站務／檢修欄位與目前工作區不符。");
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
                employee.PerpetualScheduleId = demand.Workspace.IsStation() ? previous.PerpetualScheduleId : null;
            }
            else
            {
                employee.OpeningRest = null;
                employee.OpeningSpecialRest = null;
                employee.PerpetualScheduleId = null;
            }
        }
    }

    private static void InheritBlankPerpetualScheduleIds(DemandDraft demand, MonthlySchedule? previous)
    {
        if (!demand.Workspace.IsStation() || previous is null) return;
        var previousByCode = previous.Employees.ToDictionary(x => x.EmployeeId, StringComparer.Ordinal);
        foreach (var employee in demand.Employees)
        {
            if (employee.PerpetualScheduleId is not null) continue;
            if (previousByCode.TryGetValue(employee.EmployeeCode, out var prior))
                employee.PerpetualScheduleId = prior.PerpetualScheduleId;
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
