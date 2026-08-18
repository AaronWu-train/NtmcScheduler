using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class EmployeeDemandSubmissionService(IDbContextFactory<NtmcDbContext> dbFactory, IDemandService demandService) : IEmployeeDemandSubmissionService
{
    public async Task<EmployeeDemandSubmissionDto?> GetAsync(
        WorkspaceCode workspace,
        DateOnly month,
        string employeeCode,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        month = MonthStart(month);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var submission = await Query(db).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Workspace == workspace && x.Month == month && x.EmployeeCode == employeeCode, cancellationToken);
        if (submission is null) return null;
        var importAt = await ImportCutoffAsync(db, workspace, month, cancellationToken);
        return ToDto(submission, importAt);
    }

    public async Task<EmployeeDemandSubmissionDto> UpdateLeaveRestAsync(
        WorkspaceCode workspace,
        DateOnly month,
        string employeeCode,
        int requestedLeaveRestCount,
        Guid? revisionToken,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (requestedLeaveRestCount < 0) throw new DomainValidationException("R休上限不可為負數。");
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        ServiceSupport.RequireViewer(actor);
        month = MonthStart(month);
        var employee = await db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Workspace == workspace && x.EmployeeCode == employeeCode, cancellationToken)
            ?? throw new DomainValidationException("找不到員工資料。");
        var submission = await Query(db)
            .SingleOrDefaultAsync(x => x.Workspace == workspace && x.Month == month && x.EmployeeCode == employeeCode, cancellationToken);
        if (submission is null)
        {
            submission = NewSubmission(workspace, month, employee, actor);
            db.EmployeeDemandSubmissions.Add(submission);
        }
        else
        {
            if (revisionToken is not null && submission.RevisionToken != revisionToken)
                throw new ConcurrencyConflictException("填報已被其他人修改，請重新整理。");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = Snapshot(submission);
        submission.RequestedLeaveRestCount = requestedLeaveRestCount;
        TouchSubmission(submission, employee, actor);
        ServiceSupport.AddAudit(db, actor, "EmployeeDemandSubmissionUpdated", workspace, "EmployeeDemandSubmission", submission.Id,
            before, Snapshot(submission));
        await SaveAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var importAt = await ImportCutoffAsync(db, workspace, month, cancellationToken);
        return ToDto(submission, importAt);
    }

    public async Task<EmployeeDemandSubmissionDto> UpdateAssignmentAsync(
        WorkspaceCode workspace,
        DateOnly month,
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
        ServiceSupport.RequireViewer(actor);
        month = MonthStart(month);
        if (date < month || date >= month.AddMonths(1)) throw new DomainValidationException("日格日期不在目前月份內。");
        DemandCellValidator.Validate(workspace, date, kind, requestedRest, station, shift, eventStart, eventEnd, eventDescription);
        var employee = await db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Workspace == workspace && x.EmployeeCode == employeeCode, cancellationToken)
            ?? throw new DomainValidationException("找不到員工資料。");
        if (employee.EmploymentStartDate is { } start && date < start)
            throw new DomainValidationException("到職日前不可填寫日格。");
        var submission = await Query(db)
            .SingleOrDefaultAsync(x => x.Workspace == workspace && x.Month == month && x.EmployeeCode == employeeCode, cancellationToken);
        if (submission is null)
        {
            submission = NewSubmission(workspace, month, employee, actor);
            db.EmployeeDemandSubmissions.Add(submission);
        }
        else if (submission.RevisionToken != revisionToken)
            throw new ConcurrencyConflictException("填報已被其他人修改，請重新整理。");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var assignment = submission.Assignments.SingleOrDefault(x => x.Date == date);
        var before = assignment is null ? null : AssignmentSnapshot(submission, assignment);
        if (string.IsNullOrWhiteSpace(kind) && !requestedRest)
        {
            if (assignment is not null)
            {
                submission.Assignments.Remove(assignment);
                db.EmployeeDemandSubmissionAssignments.Remove(assignment);
            }
        }
        else
        {
            if (assignment is null)
            {
                assignment = new EmployeeDemandSubmissionAssignment { SubmissionId = submission.Id, Date = date };
                submission.Assignments.Add(assignment);
                db.EmployeeDemandSubmissionAssignments.Add(assignment);
            }
            assignment.Kind = string.IsNullOrWhiteSpace(kind) || kind == "Unresolved" ? null : kind;
            assignment.RequestedRest = requestedRest;
            assignment.Station = kind == "Work" && workspace == WorkspaceCode.M ? station : null;
            assignment.Shift = kind == "Work" ? SolverScheduleMapper.ParseShift(shift).ToString() : null;
            assignment.EventStart = kind == "WorkEvent" ? eventStart : null;
            assignment.EventEnd = kind == "WorkEvent" ? eventEnd : null;
            assignment.EventDescription = kind == "WorkEvent" && !string.IsNullOrWhiteSpace(eventDescription) ? eventDescription.Trim() : null;
        }
        TouchSubmission(submission, employee, actor);
        ServiceSupport.AddAudit(db, actor, "EmployeeDemandSubmissionAssignmentUpdated", workspace, "EmployeeDemandSubmission", submission.Id,
            before, assignment is null ? null : AssignmentSnapshot(submission, assignment));
        await SaveAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var importAt = await ImportCutoffAsync(db, workspace, month, cancellationToken);
        return ToDto(submission, importAt);
    }

    public async Task<DemandSubmissionImportDto?> GetImportStatusAsync(Guid demandDraftId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await db.DemandDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == demandDraftId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        var import = await db.DemandSubmissionImports.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DemandDraftId == demandDraftId, cancellationToken);
        return import is null ? null : new(demandDraftId, import.ImportedAtUtc, import.ImportedByName);
    }

    public async Task<SubmissionImportPreviewDto> PreviewImportAsync(Guid demandDraftId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var (demand, submissions, existingImport) = await LoadImportContextAsync(db, demandDraftId, actor, cancellationToken);
        var errors = new List<string>();
        var differences = new List<string>();
        if (submissions.Count == 0)
            errors.Add("目前沒有任何員工填報可匯入。");

        var demandCodes = demand.Employees.Select(x => x.EmployeeCode).ToHashSet(StringComparer.Ordinal);
        var matched = 0;
        var late = 0;
        foreach (var submission in submissions.OrderBy(x => x.EmployeeCode))
        {
            if (!demandCodes.Contains(submission.EmployeeCode))
            {
                differences.Add($"填報員工 {submission.EmployeeCode} 不在本月 Demand，將略過。");
                continue;
            }
            matched++;
            if (existingImport is not null && submission.UpdatedAtUtc > existingImport.ImportedAtUtc) late++;
            differences.Add($"{submission.EmployeeCode} {submission.Name}：R休上限 {submission.RequestedLeaveRestCount}、{submission.Assignments.Count} 個日格（最後更新 {FormatTaipei(submission.UpdatedAtUtc)}，{submission.UpdatedByName}）");
        }

        var unmatchedDemand = demand.Employees.Count(x => submissions.All(s => s.EmployeeCode != x.EmployeeCode));
        if (unmatchedDemand > 0)
            differences.Add($"Demand 中有 {unmatchedDemand} 位員工尚無填報，匯入後維持原內容。");

        return new(errors.Count == 0 && submissions.Count > 0 && matched > 0, errors, differences, submissions.Count, matched, late);
    }

    public Task<DemandDraftDto> ImportToDemandAsync(Guid demandDraftId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default) =>
        demandService.ImportEmployeeSubmissionsAsync(demandDraftId, revisionToken, actor, cancellationToken);

    private static async Task<(DemandDraft demand, List<EmployeeDemandSubmission> submissions, DemandSubmissionImport? existingImport)> LoadImportContextAsync(
        NtmcDbContext db,
        Guid demandDraftId,
        ActorContext actor,
        CancellationToken cancellationToken,
        bool tracked = false)
    {
        var demandQuery = tracked
            ? db.DemandDrafts.AsSplitQuery().Include(x => x.Employees).ThenInclude(x => x.Assignments)
            : db.DemandDrafts.AsNoTracking().AsSplitQuery().Include(x => x.Employees).ThenInclude(x => x.Assignments);
        var demand = await demandQuery.SingleOrDefaultAsync(x => x.Id == demandDraftId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        var submissions = await Query(db).AsNoTracking()
            .Where(x => x.Workspace == demand.Workspace && x.Month == demand.Month)
            .OrderBy(x => x.EmployeeCode)
            .ToListAsync(cancellationToken);
        var existingImport = await db.DemandSubmissionImports.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DemandDraftId == demandDraftId, cancellationToken);
        return (demand, submissions, existingImport);
    }

    private static IQueryable<EmployeeDemandSubmission> Query(NtmcDbContext db) =>
        db.EmployeeDemandSubmissions.AsSplitQuery().Include(x => x.Assignments);

    private static EmployeeDemandSubmission NewSubmission(WorkspaceCode workspace, DateOnly month, Employee employee, ActorContext actor) => new()
    {
        Workspace = workspace,
        Month = month,
        EmployeeCode = employee.EmployeeCode,
        Name = employee.Name,
        Affiliation = employee.Affiliation,
        EmploymentStartDate = employee.EmploymentStartDate,
        UpdatedByUserId = actor.UserId,
        UpdatedByName = actor.UserName
    };

    private static void TouchSubmission(EmployeeDemandSubmission submission, Employee employee, ActorContext actor)
    {
        submission.Name = employee.Name;
        submission.Affiliation = employee.Affiliation;
        submission.EmploymentStartDate = employee.EmploymentStartDate;
        submission.UpdatedByUserId = actor.UserId;
        submission.UpdatedByName = actor.UserName;
        submission.UpdatedAtUtc = DateTimeOffset.UtcNow;
        submission.RevisionToken = Guid.NewGuid();
    }

    private static object Snapshot(EmployeeDemandSubmission submission) => new
    {
        submission.Month,
        submission.EmployeeCode,
        submission.Name,
        submission.RequestedLeaveRestCount,
        Assignments = submission.Assignments.Count
    };

    private static object AssignmentSnapshot(EmployeeDemandSubmission submission, EmployeeDemandSubmissionAssignment assignment) => new
    {
        submission.Month,
        submission.EmployeeCode,
        submission.Name,
        assignment.Date,
        assignment.Kind,
        assignment.RequestedRest,
        assignment.Station,
        assignment.Shift,
        assignment.EventStart,
        assignment.EventEnd,
        assignment.EventDescription
    };

    private static EmployeeDemandSubmissionDto ToDto(EmployeeDemandSubmission submission, DateTimeOffset? importCutoff) => new(
        submission.Id,
        submission.Workspace,
        submission.Month,
        submission.EmployeeCode,
        submission.Name,
        submission.Affiliation,
        submission.EmploymentStartDate,
        submission.RequestedLeaveRestCount,
        submission.RevisionToken,
        submission.UpdatedAtUtc,
        submission.UpdatedByName,
        importCutoff is not null && submission.UpdatedAtUtc > importCutoff,
        submission.Assignments.OrderBy(x => x.Date).Select(x => new EmployeeDemandSubmissionAssignmentDto(
            x.Id, x.Date, x.Kind, x.RequestedRest, x.Station, x.Shift, x.EventStart, x.EventEnd, x.EventDescription)).ToArray());

    private static async Task<DateTimeOffset?> ImportCutoffAsync(NtmcDbContext db, WorkspaceCode workspace, DateOnly month, CancellationToken cancellationToken)
    {
        var demandId = await db.DemandDrafts.AsNoTracking()
            .Where(x => x.Workspace == workspace && x.Month == month)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (demandId is null) return null;
        return await db.DemandSubmissionImports.AsNoTracking()
            .Where(x => x.DemandDraftId == demandId)
            .Select(x => (DateTimeOffset?)x.ImportedAtUtc)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static DateOnly MonthStart(DateOnly month) => new(month.Year, month.Month, 1);

    private static string FormatTaipei(DateTimeOffset value) =>
        value.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm");

    private static async Task SaveAsync(NtmcDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("填報資料已失效，請重新整理後再儲存。");
        }
    }
}
