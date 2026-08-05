using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Time;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class DraftService : IDraftService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public DraftService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<WideTableDto> GetAsync(long draftId, CancellationToken ct = default)
    {
        var draft = await _db.DraftSchedules.AsNoTracking()
            .Include(d => d.Run)
            .FirstOrDefaultAsync(d => d.Id == draftId, ct)
            ?? throw new KeyNotFoundException($"找不到 Draft {draftId}");

        var month = YearMonth.Parse(draft.Run!.TargetMonth);
        var assignments = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.Draft && a.OwnerId == draftId)
            .ToListAsync(ct);
        var dates = assignments.Select(a => a.Date).Distinct().OrderBy(d => d).ToList();
        if (dates.Count == 0)
            dates = Enumerable.Range(0, month.LastDay.DayNumber - month.FirstDay.DayNumber + 1)
                .Select(i => month.FirstDay.AddDays(i)).ToList();

        var empIds = assignments.Select(a => a.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var rows = new List<WideTableRowDto>();
        foreach (var empId in empIds.OrderBy(x => x))
        {
            employees.TryGetValue(empId, out var emp);
            var cells = assignments.Where(a => a.EmployeeId == empId)
                .ToDictionary(
                    a => a.Date,
                    a => new CellDto(
                        a.Date,
                        DayState.ParseDisplay(a.State),
                        a.Date > month.LastDay,
                        true,
                        Array.Empty<string>()));
            string? group = null;
            if (emp?.Unit == Unit.M && emp.HomeStation is { } hs && StationConfig.StationGroup.ContainsKey(hs))
                group = StationConfig.GroupOf(hs);

            rows.Add(new WideTableRowDto(
                empId,
                emp?.Name ?? empId,
                emp?.HomeStation,
                group,
                cells));
        }

        return new WideTableDto(draft.Run.Unit, month, month.LastDay, dates, rows, IsEditable: true);
    }

    public Task<IReadOnlyList<CellOptionDto>> GetCellOptionsAsync(
        long draftId, string employeeId, DateOnly date, CancellationToken ct = default)
    {
        IReadOnlyList<CellOptionDto> options =
        [
            new(DayState.Work(ShiftType.Morning), Array.Empty<string>()),
            new(DayState.Work(ShiftType.Afternoon), Array.Empty<string>()),
            new(DayState.Work(ShiftType.Night), Array.Empty<string>()),
            new(DayState.Rest, Array.Empty<string>()),
            new(DayState.RStar, Array.Empty<string>()),
            new(DayState.HolidayRest, Array.Empty<string>()),
            new(DayState.X, Array.Empty<string>())
        ];
        return Task.FromResult(options);
    }

    public async Task<DraftValidationDto> ApplyEditAsync(
        long draftId, string employeeId, DateOnly date, DayState state, string op,
        CancellationToken ct = default)
    {
        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a =>
                a.OwnerType == AssignmentOwnerType.Draft && a.OwnerId == draftId
                && a.EmployeeId == employeeId && a.Date == date, ct)
            ?? throw new KeyNotFoundException($"找不到格子：{employeeId} @ {date}");

        var before = assignment.State;
        var after = state.ToDisplay();
        assignment.State = after;

        var seq = await _db.DraftEdits.Where(e => e.DraftId == draftId).Select(e => (int?)e.Seq).MaxAsync(ct) ?? 0;
        _db.DraftEdits.Add(new DraftEdit
        {
            DraftId = draftId,
            Seq = seq + 1,
            EmployeeId = employeeId,
            Date = date,
            BeforeState = before,
            AfterState = after,
            Operator = op,
            At = TaipeiTime.Now
        });
        _audit.Add(op, "Draft.Edit", "DraftSchedule", draftId.ToString(),
            before: new { employeeId, date, before },
            after: new { employeeId, date, after });
        await _db.SaveChangesAsync(ct);
        return await RevalidateAsync(draftId, ct);
    }

    public async Task<DraftValidationDto> UndoAsync(long draftId, string op, CancellationToken ct = default)
    {
        var last = await _db.DraftEdits
            .Where(e => e.DraftId == draftId)
            .OrderByDescending(e => e.Seq)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("沒有可復原的修改");

        var assignment = await _db.Assignments.FirstAsync(a =>
            a.OwnerType == AssignmentOwnerType.Draft && a.OwnerId == draftId
            && a.EmployeeId == last.EmployeeId && a.Date == last.Date, ct);
        assignment.State = last.BeforeState;
        _db.DraftEdits.Remove(last);
        _audit.Add(op, "Draft.Undo", "DraftSchedule", draftId.ToString(), before: last);
        await _db.SaveChangesAsync(ct);
        return await RevalidateAsync(draftId, ct);
    }

    public Task<DraftValidationDto> RevalidateAsync(long draftId, CancellationToken ct = default)
    {
        _ = draftId;
        _ = ct;
        return Task.FromResult(new DraftValidationDto(
            false,
            Array.Empty<RuleMetricDto>(),
            null,
            null,
            [new PublishBlockerDto("VALIDATION_NOT_IMPLEMENTED", "Draft 完整規則驗證尚未實作，禁止發布")],
            Array.Empty<Core.Evaluation.ViolationItem>()));
    }
}
