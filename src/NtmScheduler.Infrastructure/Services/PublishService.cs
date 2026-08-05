using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Time;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class PublishService : IPublishService
{
    private readonly NtmDbContext _db;
    private readonly IDraftService _drafts;
    private readonly AuditWriter _audit;

    public PublishService(NtmDbContext db, IDraftService drafts, AuditWriter audit)
    {
        _db = db;
        _drafts = drafts;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PublishBlockerDto>> CheckAsync(long draftId, CancellationToken ct = default)
    {
        var validation = await _drafts.RevalidateAsync(draftId, ct);
        var blockers = validation.PublishBlockers.ToList();

        var draft = await _db.DraftSchedules.AsNoTracking()
            .Include(d => d.Run)
            .FirstOrDefaultAsync(d => d.Id == draftId, ct);
        if (draft is null)
        {
            blockers.Add(new PublishBlockerDto("DRAFT_NOT_FOUND", $"找不到 Draft {draftId}"));
            return blockers;
        }

        var hasAssignments = await _db.Assignments.AsNoTracking()
            .AnyAsync(a => a.OwnerType == AssignmentOwnerType.Draft && a.OwnerId == draftId, ct);
        if (!hasAssignments)
            blockers.Add(new PublishBlockerDto("NO_ASSIGNMENTS", "Draft 尚無班表資料"));

        if (!validation.P0Passed)
            blockers.Add(new PublishBlockerDto("P0_FAILED", "存在 P0 硬規則違反"));

        return blockers;
    }

    public async Task<long> PublishAsync(long draftId, string op, CancellationToken ct = default)
    {
        var blockers = await CheckAsync(draftId, ct);
        if (blockers.Count > 0)
            throw new InvalidOperationException("發布檢查未通過：" + string.Join("；", blockers.Select(b => b.Message)));

        var draft = await _db.DraftSchedules
            .Include(d => d.Run)
            .FirstAsync(d => d.Id == draftId, ct);
        var run = draft.Run ?? await _db.ScheduleRuns.FirstAsync(r => r.Id == draft.RunId, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var current = await _db.OfficialScheduleVersions
            .Where(v => v.Unit == run.Unit && v.Month == run.TargetMonth && v.IsCurrent)
            .ToListAsync(ct);
        foreach (var v in current)
            v.IsCurrent = false;

        var nextNo = await _db.OfficialScheduleVersions
            .Where(v => v.Unit == run.Unit && v.Month == run.TargetMonth)
            .Select(v => (int?)v.VersionNo)
            .MaxAsync(ct) ?? 0;

        var version = new OfficialScheduleVersion
        {
            Unit = run.Unit,
            Month = run.TargetMonth,
            VersionNo = nextNo + 1,
            PublishedAt = TaipeiTime.Now,
            Operator = op,
            IsCurrent = true
        };
        _db.OfficialScheduleVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        var assignments = await _db.Assignments
            .AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.Draft && a.OwnerId == draftId)
            .ToListAsync(ct);

        foreach (var a in assignments)
        {
            _db.Assignments.Add(new Assignment
            {
                OwnerType = AssignmentOwnerType.PublishedVersion,
                OwnerId = version.Id,
                EmployeeId = a.EmployeeId,
                Date = a.Date,
                State = a.State
            });
        }

        _audit.Add(op, "Publish", "OfficialScheduleVersion", version.Id.ToString(),
            before: new { draftId }, after: new { version.Id, version.VersionNo });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return version.Id;
    }

    public async Task<IReadOnlyList<VersionDto>> GetVersionsAsync(
        Unit unit, YearMonth month, CancellationToken ct = default)
    {
        var rows = await _db.OfficialScheduleVersions.AsNoTracking()
            .Where(v => v.Unit == unit && v.Month == month.ToString())
            .OrderByDescending(v => v.VersionNo)
            .ToListAsync(ct);

        return rows.Select(v => new VersionDto(
            v.Id, v.Unit, YearMonth.Parse(v.Month), v.VersionNo, v.PublishedAt, v.Operator, v.IsCurrent)).ToList();
    }

    public async Task<WideTableDto> GetVersionAsync(long versionId, CancellationToken ct = default)
    {
        var version = await _db.OfficialScheduleVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new KeyNotFoundException($"找不到版本 {versionId}");

        var month = YearMonth.Parse(version.Month);
        var assignments = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.PublishedVersion && a.OwnerId == versionId)
            .ToListAsync(ct);
        var dates = assignments.Select(a => a.Date).Distinct().OrderBy(d => d).ToList();
        var empIds = assignments.Select(a => a.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var rows = new List<WideTableRowDto>();
        foreach (var empId in empIds.OrderBy(x => x))
        {
            employees.TryGetValue(empId, out var emp);
            string? group = null;
            if (emp?.Unit == Unit.M && emp.HomeStation is { } hs && StationConfig.StationGroup.ContainsKey(hs))
                group = StationConfig.GroupOf(hs);

            var cells = assignments.Where(a => a.EmployeeId == empId)
                .ToDictionary(
                    a => a.Date,
                    a => new CellDto(a.Date, DayState.ParseDisplay(a.State), a.Date > month.LastDay, false, Array.Empty<string>()));
            rows.Add(new WideTableRowDto(empId, emp?.Name ?? empId, emp?.HomeStation, group, cells));
        }

        return new WideTableDto(version.Unit, month, month.LastDay, dates, rows, IsEditable: false);
    }
}
