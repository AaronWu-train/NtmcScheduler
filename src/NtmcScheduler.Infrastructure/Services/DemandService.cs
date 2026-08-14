using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class DemandService(NtmcDbContext db) : IDemandService
{
    public async Task<IReadOnlyList<DateOnly>> ListMonthsAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        return await db.DemandDrafts.AsNoTracking().Where(x => x.Workspace == workspace).OrderByDescending(x => x.Month).Select(x => x.Month).ToArrayAsync(cancellationToken);
    }

    public async Task<DemandDraftDto?> GetAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        month = MonthStart(month);
        var demand = await Query().AsNoTracking().SingleOrDefaultAsync(x => x.Workspace == workspace && x.Month == month, cancellationToken);
        return demand is null ? null : ServiceSupport.ToDto(demand);
    }

    public async Task<DemandDraftDto> CreateAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireEditor(actor, workspace);
        month = MonthStart(month);
        if (await db.DemandDrafts.AnyAsync(x => x.Workspace == workspace && x.Month == month, cancellationToken))
            throw new DomainValidationException("這個月份已經有 Demand。");
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
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceSupport.ToDto(demand);
    }

    public async Task DeleteAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        var demand = await db.DemandDrafts.Include(x => x.UploadedPreviousSchedule).SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到 Demand。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("Demand 已被其他人修改，請重新整理。");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var previous = demand.UploadedPreviousSchedule;
        ServiceSupport.AddAudit(db, actor, "DemandDeleted", demand.Workspace, "DemandDraft", demand.Id,
            new { demand.Month, EmployeeCount = await db.DemandEmployees.CountAsync(x => x.DemandDraftId == demand.Id, cancellationToken) }, null);
        db.DemandDrafts.Remove(demand);
        if (previous is not null) db.UploadedPreviousSchedules.Remove(previous);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DemandDraftDto> UpdateEmployeeAsync(
        Guid demandEmployeeId,
        string? monthlyShift,
        int? openingRest,
        int? openingSpecialRest,
        int requestedLeaveRestCount,
        string? perpetualScheduleId,
        Guid revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var employee = await db.DemandEmployees.Include(x => x.DemandDraft).SingleOrDefaultAsync(x => x.Id == demandEmployeeId, cancellationToken)
            ?? throw new DomainValidationException("找不到月份員工資料。");
        var demand = employee.DemandDraft;
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("Demand 已被其他人修改，請重新整理。");
        if ((openingRest is null) != (openingSpecialRest is null) || openingRest < 0 || openingSpecialRest < 0 || requestedLeaveRestCount < 0)
            throw new DomainValidationException("月初 R/R1 必須同時填寫且不可為負數；R休上限不可為負數。");
        if (demand.Workspace == WorkspaceCode.T && SolverScheduleMapper.ParseShift(monthlyShift) is null)
            throw new DomainValidationException("T 月班別必須為早、午或夜。");
        if (demand.Workspace == WorkspaceCode.M && !string.IsNullOrWhiteSpace(monthlyShift))
            throw new DomainValidationException("M 不可設定 T 月班別。");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = new { employee.MonthlyShift, employee.OpeningRest, employee.OpeningSpecialRest, employee.RequestedLeaveRestCount, employee.PerpetualScheduleId };
        employee.MonthlyShift = string.IsNullOrWhiteSpace(monthlyShift) ? null : SolverScheduleMapper.ParseShift(monthlyShift).ToString();
        employee.OpeningRest = openingRest;
        employee.OpeningSpecialRest = openingSpecialRest;
        employee.RequestedLeaveRestCount = requestedLeaveRestCount;
        employee.PerpetualScheduleId = string.IsNullOrWhiteSpace(perpetualScheduleId) ? null : perpetualScheduleId.Trim();
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandEmployeeUpdated", demand.Workspace, "DemandEmployee", employee.Id, before,
            new { employee.MonthlyShift, employee.OpeningRest, employee.OpeningSpecialRest, employee.RequestedLeaveRestCount, employee.PerpetualScheduleId });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceSupport.ToDto(await Query().SingleAsync(x => x.Id == demand.Id, cancellationToken));
    }

    public async Task<ImportPreviewDto> PreviewDemandImportAsync(Guid demandId, Stream csv, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        var demand = await Query().AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到 Demand。");
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
        Guid demandEmployeeId,
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
        var employee = await db.DemandEmployees.Include(x => x.Assignments).Include(x => x.DemandDraft)
            .SingleOrDefaultAsync(x => x.Id == demandEmployeeId, cancellationToken)
            ?? throw new DomainValidationException("找不到月份員工資料。");
        var demand = employee.DemandDraft;
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("Demand 已被其他人修改，請重新整理。");
        if (date < demand.Month || date >= demand.Month.AddMonths(1)) throw new DomainValidationException("日格日期不在 Demand 月份內。");
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
            assignment ??= new DemandAssignment { DemandEmployeeId = employee.Id, Date = date };
            if (assignment.Id == Guid.Empty || !employee.Assignments.Contains(assignment)) employee.Assignments.Add(assignment);
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
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceSupport.ToDto(await Query().SingleAsync(x => x.Id == demand.Id, cancellationToken));
    }

    public async Task ImportDemandAsync(Guid demandId, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        var demand = await Query().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到 Demand。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("Demand 已被其他人修改，請重新整理。");
        var schedule = await ParseMonthlyAsync(csv, demand, false, cancellationToken);
        ValidateWorkspace(schedule, demand.Workspace);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = new { Employees = demand.Employees.Count, Assignments = demand.Employees.Sum(x => x.Assignments.Count) };
        db.DemandEmployees.RemoveRange(demand.Employees);
        demand.Employees = schedule.Employees.Select(SolverScheduleMapper.ToDemandEmployee).ToList();
        db.DemandEmployees.AddRange(demand.Employees);
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "DemandCsvImported", demand.Workspace, "DemandDraft", demand.Id, before,
            new { Employees = demand.Employees.Count, Assignments = demand.Employees.Sum(x => x.Assignments.Count) });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UploadPreviousAsync(Guid demandId, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        var demand = await Query().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到 Demand。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("Demand 已被其他人修改，請重新整理。");
        var schedule = await ParseMonthlyAsync(csv, demand, true, cancellationToken);
        ValidateWorkspace(schedule, demand.Workspace);
        var upload = new UploadedPreviousSchedule
        {
            Workspace = demand.Workspace,
            Month = demand.Month.AddMonths(-1),
            ParsedScheduleJson = JsonSerializer.Serialize(schedule, ServiceSupport.JsonOptions)
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var previousUpload = demand.UploadedPreviousSchedule;
        db.UploadedPreviousSchedules.Add(upload);
        demand.UploadedPreviousSchedule = upload;
        demand.PreviousSource = PreviousScheduleSource.Upload;
        demand.PreviousAdoptedScheduleVersionId = null;
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
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "PreviousScheduleUploaded", demand.Workspace, "DemandDraft", demand.Id, null,
            new { upload.Id, upload.Month, EmployeeCount = schedule.Employees.Count });
        if (previousUpload is not null) db.UploadedPreviousSchedules.Remove(previousUpload);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UploadPerpetualScheduleAsync(Guid demandId, Stream csv, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        var demand = await Query().SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到 Demand。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.Workspace != WorkspaceCode.M) throw new DomainValidationException("只有 M 可上傳八週萬年班表。");
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("Demand 已被其他人修改，請重新整理。");
        var schedule = await UploadFile.ParseAsync(csv, ScheduleCsv.ReadMPerpetualSchedule, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        demand.PerpetualScheduleJson = JsonSerializer.Serialize(schedule, ServiceSupport.JsonOptions);
        Touch(demand, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "PerpetualScheduleUploaded", demand.Workspace, "DemandDraft", demand.Id, null,
            new { PatternCount = schedule.Patterns.Count });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<DemandDraft> Query() => db.DemandDrafts
        .Include(x => x.ConfigurationRevision).ThenInclude(x => x.NonStandardShifts)
        .Include(x => x.Employees).ThenInclude(x => x.Assignments)
        .Include(x => x.UploadedPreviousSchedule);

    private async Task<MonthlySchedule> ParseMonthlyAsync(Stream csv, DemandDraft demand, bool historical, CancellationToken cancellationToken)
    {
        var shifts = SolverScheduleMapper.ToNonStandardShifts(demand.ConfigurationRevision);
        var month = historical ? demand.Month.AddMonths(-1) : demand.Month;
        return await UploadFile.ParseAsync(csv, path => ScheduleCsv.ReadMonthly(path, month, shifts, historical), cancellationToken);
    }

    private static void ValidateWorkspace(MonthlySchedule schedule, WorkspaceCode workspace)
    {
        var isT = schedule.Employees.Any(x => x.Ability is not null || x.MonthlyShift is not null);
        if (isT != (workspace == WorkspaceCode.T)) throw new DomainValidationException("CSV 的 M/T 欄位與目前工作區不符。");
    }

    private static DateOnly MonthStart(DateOnly month) => new(month.Year, month.Month, 1);

    private static void ValidateDemandCell(WorkspaceCode workspace, DateOnly date, string? kind, bool requestedRest, string? station, string? shift, DateTimeOffset? eventStart, DateTimeOffset? eventEnd, string? description)
    {
        if (kind is not (null or "" or "Unresolved" or "Work" or "Rest" or "SpecialRest" or "LeaveRest" or "WorkEvent"))
            throw new DomainValidationException("不支援的 Demand 日格狀態。");
        if (kind == "LeaveRest" && !requestedRest) throw new DomainValidationException("Demand 的 R休 必須標記為 R*。");
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
    private static void Touch(DemandDraft demand, Guid actorId)
    {
        demand.UpdatedByUserId = actorId;
        demand.UpdatedAtUtc = DateTimeOffset.UtcNow;
        demand.RevisionToken = Guid.NewGuid();
    }
}
