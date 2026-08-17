using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class ScheduleService(
    NtmcDbContext db,
    IScheduleValidationService validation) : IScheduleService
{
    public async Task<IReadOnlyList<ScheduleMonthDto>> ListMonthsAsync(WorkspaceCode workspace, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        var versions = await db.ScheduleVersions.AsNoTracking().Include(x => x.SourceRun).Where(x => x.Workspace == workspace && !x.IsArchived).ToListAsync(cancellationToken);
        var adopted = await db.AdoptedSchedules.AsNoTracking().Include(x => x.ScheduleVersion).ThenInclude(x => x.SourceRun).Where(x => x.Workspace == workspace).ToDictionaryAsync(x => x.Month, cancellationToken);
        return versions.GroupBy(x => x.Month).OrderByDescending(x => x.Key).Select(group => new ScheduleMonthDto(
            group.Key,
            group.Count(),
            adopted.GetValueOrDefault(group.Key) is { } current ? ToDto(current.ScheduleVersion, true) : null,
            group.Max(x => x.UpdatedAtUtc))).ToArray();
    }

    public async Task<IReadOnlyList<ScheduleVersionDto>> ListVersionsAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        month = new(month.Year, month.Month, 1);
        var adoptedId = await db.AdoptedSchedules.AsNoTracking().Where(x => x.Workspace == workspace && x.Month == month)
            .Select(x => (Guid?)x.ScheduleVersionId).SingleOrDefaultAsync(cancellationToken);
        var versions = await db.ScheduleVersions.AsNoTracking().Include(x => x.SourceRun)
            .Where(x => x.Workspace == workspace && x.Month == month && (includeArchived || !x.IsArchived))
            .ToListAsync(cancellationToken);
        return versions.OrderByDescending(x => x.CreatedAtUtc).Select(x => ToDto(x, x.Id == adoptedId)).ToArray();
    }

    public async Task<ScheduleDetailDto> GetAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        var version = await VersionQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new DomainValidationException("找不到班表版本。");
        var adopted = await db.AdoptedSchedules.AsNoTracking().AnyAsync(x => x.ScheduleVersionId == versionId, cancellationToken);
        var result = await validation.ValidateAsync(versionId, actor, cancellationToken);
        return ToDetail(version, adopted, result.Issues, result.Stats);
    }

    public async Task<ScheduleDetailDto> UpdateAssignmentAsync(
        Guid versionId,
        Guid assignmentId,
        string kind,
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
        var version = await VersionQuery().SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new DomainValidationException("找不到班表版本。");
        ServiceSupport.RequireEditor(actor, version.Workspace);
        if (version.IsArchived) throw new DomainValidationException("封存班表不可修改。");
        if (version.RevisionToken != revisionToken) throw new ConcurrencyConflictException("班表已被其他人修改，請重新整理。");
        var assignment = version.Employees.SelectMany(x => x.Assignments).SingleOrDefault(x => x.Id == assignmentId)
            ?? throw new DomainValidationException("找不到日格。");
        ValidateCell(version.Workspace, assignment.Date, kind, requestedRest, station, shift, eventStart, eventEnd, eventDescription);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = AssignmentSnapshot(assignment);
        assignment.Kind = kind;
        assignment.RequestedRest = requestedRest;
        assignment.Station = kind == "Work" && version.Workspace == WorkspaceCode.M ? station : null;
        assignment.Shift = kind == "Work" ? SolverScheduleMapper.ParseShift(shift).ToString() : null;
        assignment.EventStart = kind == "WorkEvent" ? eventStart : null;
        assignment.EventEnd = kind == "WorkEvent" ? eventEnd : null;
        assignment.EventDescription = kind == "WorkEvent" && !string.IsNullOrWhiteSpace(eventDescription) ? eventDescription.Trim() : null;
        Touch(version, actor.UserId);
        await db.SaveChangesAsync(cancellationToken);
        var checkedSchedule = await validation.ValidateAsync(version.Id, actor, cancellationToken);
        version.HasErrors = checkedSchedule.Issues.Any(x => x.Severity == ValidationSeverity.Error);
        version.WarningCount = checkedSchedule.Issues.Count(x => x.Severity == ValidationSeverity.Warning);
        ServiceSupport.AddAudit(db, actor, "ScheduleAssignmentUpdated", version.Workspace, "ScheduleAssignment", assignment.Id,
            before, AssignmentSnapshot(assignment));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var adopted = await db.AdoptedSchedules.AsNoTracking().AnyAsync(x => x.ScheduleVersionId == version.Id, cancellationToken);
        return ToDetail(version, adopted, checkedSchedule.Issues, checkedSchedule.Stats);
    }

    public async Task AdoptAsync(Guid versionId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        var version = await db.ScheduleVersions.SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new DomainValidationException("找不到班表版本。");
        ServiceSupport.RequireEditor(actor, version.Workspace);
        if (version.IsArchived) throw new DomainValidationException("封存班表不可採用。");
        if (version.RevisionToken != revisionToken) throw new ConcurrencyConflictException("班表已被其他人修改，請重新整理。");
        var checkedSchedule = await validation.ValidateAsync(version.Id, actor, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        version.HasErrors = false;
        version.WarningCount = checkedSchedule.Issues.Count;
        Touch(version, actor.UserId);
        var adopted = await db.AdoptedSchedules.SingleOrDefaultAsync(x => x.Workspace == version.Workspace && x.Month == version.Month, cancellationToken);
        var before = adopted?.ScheduleVersionId;
        if (adopted is null)
            db.AdoptedSchedules.Add(new() { Workspace = version.Workspace, Month = version.Month, ScheduleVersionId = version.Id, AdoptedByUserId = actor.UserId });
        else
        {
            adopted.ScheduleVersionId = version.Id;
            adopted.AdoptedByUserId = actor.UserId;
            adopted.AdoptedAtUtc = DateTimeOffset.UtcNow;
        }
        ServiceSupport.AddAudit(db, actor, "ScheduleAdopted", version.Workspace, "ScheduleVersion", version.Id, new { ScheduleVersionId = before }, new { ScheduleVersionId = version.Id });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ArchiveAsync(Guid versionId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        var version = await db.ScheduleVersions.SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new DomainValidationException("找不到班表版本。");
        ServiceSupport.RequireEditor(actor, version.Workspace);
        if (version.RevisionToken != revisionToken) throw new ConcurrencyConflictException("班表已被其他人修改，請重新整理。");
        if (await db.AdoptedSchedules.AnyAsync(x => x.ScheduleVersionId == versionId, cancellationToken))
            throw new DomainValidationException("請先採用其他班表，才能封存目前採用的班表。");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var before = ToDto(version, false);
        version.IsArchived = true;
        Touch(version, actor.UserId);
        ServiceSupport.AddAudit(db, actor, "ScheduleArchived", version.Workspace, "ScheduleVersion", version.Id, before, ToDto(version, false));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<byte[]> ExportCsvAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        var version = await VersionQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new DomainValidationException("找不到班表版本。");
        var path = Path.Combine(Path.GetTempPath(), $"ntmc-export-{Guid.NewGuid():N}.csv");
        try
        {
            ScheduleCsv.WriteMonthly(path, SolverScheduleMapper.ToMonthlySchedule(version));
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            ServiceSupport.AddAudit(db, actor, "ScheduleCsvDownloaded", version.Workspace, "ScheduleVersion", version.Id, null, new { Bytes = bytes.Length });
            await db.SaveChangesAsync(cancellationToken);
            return bytes;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public async Task<byte[]> ExportExternalCsvAsync(Guid versionId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        var version = await db.ScheduleVersions.AsNoTracking().Include(x => x.ExternalAssignments)
            .SingleOrDefaultAsync(x => x.Id == versionId, cancellationToken)
            ?? throw new DomainValidationException("找不到班表版本。");
        if (version.Workspace != WorkspaceCode.M || version.ExternalAssignments.Count == 0)
            throw new DomainValidationException("這份班表沒有 M 外派資料。");
        var lines = new List<string> { "日期,車站,班別,人數" };
        lines.AddRange(version.ExternalAssignments.OrderBy(x => x.Date).ThenBy(x => x.Station).ThenBy(x => x.Shift).Select(x =>
            string.Join(',', x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Station, ShiftText(x.Shift), x.Count.ToString(CultureInfo.InvariantCulture))));
        var path = Path.Combine(Path.GetTempPath(), $"ntmc-external-{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(true), cancellationToken);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            ServiceSupport.AddAudit(db, actor, "ExternalScheduleCsvDownloaded", version.Workspace, "ScheduleVersion", version.Id, null, new { Bytes = bytes.Length });
            await db.SaveChangesAsync(cancellationToken);
            return bytes;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private IQueryable<ScheduleVersion> VersionQuery() => db.ScheduleVersions
        .AsSplitQuery()
        .Include(x => x.ConfigurationRevision).ThenInclude(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
        .Include(x => x.ConfigurationRevision).ThenInclude(x => x.NonStandardShifts)
        .Include(x => x.Employees).ThenInclude(x => x.Assignments)
        .Include(x => x.ExternalAssignments);

    private static ScheduleDetailDto ToDetail(ScheduleVersion version, bool adopted, IReadOnlyList<ValidationIssue> issues, IReadOnlyList<ScheduleEmployeeStats> stats)
    {
        var schedule = SolverScheduleMapper.ToMonthlySchedule(version);
        var scheduleEmployees = schedule.Employees.ToDictionary(x => x.EmployeeId, StringComparer.Ordinal);
        return new(
            ToDto(version, adopted),
            version.Employees.OrderBy(x => x.EmployeeCode).Select(employee => new ScheduleEmployeeInfoDto(
                employee.Id, employee.EmployeeCode, employee.Name, employee.Affiliation, employee.EmploymentStartDate,
                employee.Ability, employee.MonthlyShift, employee.OpeningRest, employee.OpeningSpecialRest,
                ScheduleCsv.MonthlyRow(schedule, scheduleEmployees[employee.EmployeeCode]))).ToArray(),
            version.Employees.OrderBy(x => x.EmployeeCode).SelectMany(employee => employee.Assignments.OrderBy(x => x.Date).Select(assignment => new ScheduleAssignmentDto(
                assignment.Id, employee.Id, employee.EmployeeCode, employee.Name, assignment.Date, assignment.Kind, assignment.RequestedRest,
                assignment.Station, assignment.Shift, assignment.EventStart, assignment.EventEnd, assignment.EventDescription))).ToArray(),
            version.ExternalAssignments.Select(x => new ExternalAssignmentDto(x.Date, x.Station, x.Shift, x.Count)).ToArray(),
            stats,
            IntervalStats(version),
            Coverage(version),
            issues);
    }

    private static IReadOnlyList<ScheduleIntervalStatsDto> IntervalStats(ScheduleVersion version)
    {
        var monthEnd = version.Month.AddMonths(1).AddDays(-1);
        return version.Employees.SelectMany(employee => version.ConfigurationRevision.RestIntervals
            .Where(interval => interval.Start <= monthEnd && interval.End >= version.Month)
            .Select(interval =>
            {
                var start = interval.Start < version.Month ? version.Month : interval.Start;
                var end = interval.End > monthEnd ? monthEnd : interval.End;
                var cells = employee.Assignments.Where(cell => cell.Date >= start && cell.Date <= end).ToArray();
                return new ScheduleIntervalStatsDto(employee.EmployeeCode, interval.Start, interval.End,
                    (interval.Start < version.Month ? employee.OpeningRest ?? 0 : 0) + cells.Count(cell => cell.Kind == "Rest"),
                    (interval.Start < version.Month ? employee.OpeningSpecialRest ?? 0 : 0) + cells.Count(cell => cell.Kind == "SpecialRest"),
                    interval.NationalHolidays.Count);
            })).ToArray();
    }

    private static IReadOnlyList<ScheduleCoverageDto> Coverage(ScheduleVersion version)
    {
        if (version.Workspace != WorkspaceCode.M) return [];
        var monthEnd = version.Month.AddMonths(1).AddDays(-1);
        string[] stations = ["LB01", "LB02", "LB03", "LB04", "LB05", "LB06", "LB07", "LB08", "LB09", "LB10", "LB11", "LB12"];
        string[] shifts = ["Early", "Afternoon", "Night"];
        var result = new List<ScheduleCoverageDto>();
        for (var date = version.Month; date <= monthEnd; date = date.AddDays(1))
        foreach (var station in stations)
        foreach (var shift in shifts)
        {
            var required = shift is "Early" or "Afternoon" ? 1 : station is "LB01" or "LB06" or "LB08" or "LB12" ? 1 : 0;
            var allowsMultiple = (station is "LB01" or "LB06" or "LB07" or "LB12") &&
                shift is "Early" or "Afternoon";
            var internalCount = version.Employees.SelectMany(x => x.Assignments).Count(x => x.Date == date && x.Kind == "Work" && x.Station == station && x.Shift == shift);
            var externalCount = version.ExternalAssignments.Where(x => x.Date == date && x.Station == station && x.Shift == shift).Sum(x => x.Count);
            result.Add(new(date, station, shift, required, allowsMultiple, internalCount, externalCount));
        }
        return result;
    }

    private static ScheduleVersionDto ToDto(ScheduleVersion version, bool adopted) => new(
        version.Id, version.Workspace, version.Month, version.Name, version.SourceStatus, adopted, version.IsArchived,
        version.HasErrors, version.WarningCount, version.CreatedAtUtc, version.UpdatedAtUtc, version.RevisionToken, version.ConfigurationRevisionId,
        Objectives(version));

    private static IReadOnlyList<ObjectiveScoreDto> Objectives(ScheduleVersion version)
    {
        if (version.CandidateIndex is not { } index || string.IsNullOrWhiteSpace(version.SourceRun?.ResultDetailsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<ScheduleRunCandidateDto>>(version.SourceRun.ResultDetailsJson, ServiceSupport.JsonOptions)?
                .FirstOrDefault(candidate => candidate.Number == index + 1)?.Objectives ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static object AssignmentSnapshot(ScheduleAssignment assignment) => new
    {
        assignment.Kind,
        assignment.RequestedRest,
        assignment.Station,
        assignment.Shift,
        assignment.EventStart,
        assignment.EventEnd,
        assignment.EventDescription
    };

    private static void Touch(ScheduleVersion version, Guid actorId)
    {
        version.UpdatedByUserId = actorId;
        version.UpdatedAtUtc = DateTimeOffset.UtcNow;
        version.RevisionToken = Guid.NewGuid();
    }

    private static void ValidateCell(
        WorkspaceCode workspace,
        DateOnly date,
        string kind,
        bool requestedRest,
        string? station,
        string? shift,
        DateTimeOffset? eventStart,
        DateTimeOffset? eventEnd,
        string? eventDescription)
    {
        if (kind is not ("Work" or "Rest" or "SpecialRest" or "LeaveRest" or "WorkEvent"))
            throw new DomainValidationException("不支援的日格狀態。");
        if (kind == "LeaveRest" && !requestedRest) throw new DomainValidationException("R休 必須保留 R* 標記。");
        if (requestedRest && kind is not ("Rest" or "SpecialRest" or "LeaveRest"))
            throw new DomainValidationException("R* 標記只能套用在休假日格。");
        if (kind == "Work")
        {
            if (SolverScheduleMapper.ParseShift(shift) is null) throw new DomainValidationException("正常班必須指定班別。");
            if (workspace == WorkspaceCode.M && !IsMStation(station)) throw new DomainValidationException("M 正常班車站必須為 LB01–LB12。");
            if (workspace == WorkspaceCode.T && !string.IsNullOrWhiteSpace(station)) throw new DomainValidationException("T 正常班不可指定車站。");
        }
        if (kind == "WorkEvent")
        {
            if (eventStart is null || eventEnd is null || eventEnd <= eventStart || eventEnd - eventStart > TimeSpan.FromHours(24) ||
                eventStart.Value.Offset != TimeSpan.FromHours(8) || eventEnd.Value.Offset != TimeSpan.FromHours(8))
                throw new DomainValidationException("X 必須使用台北時間，結束晚於開始且長度不超過 24 小時。");
            if (DateOnly.FromDateTime(eventStart.Value.DateTime) != date)
                throw new DomainValidationException("X 必須歸在台北時間的開始日期。");
            if (eventDescription?.Length > 500) throw new DomainValidationException("X 說明不可超過 500 字元。");
        }
    }
    private static bool IsMStation(string? station) => station is not null && station.Length == 4 &&
        station.StartsWith("LB", StringComparison.Ordinal) && int.TryParse(station[2..], out var number) && number is >= 1 and <= 12;

    private static string ShiftText(string shift) => shift switch { "Early" => "早", "Afternoon" => "小", "Night" => "夜", _ => "" };
}
