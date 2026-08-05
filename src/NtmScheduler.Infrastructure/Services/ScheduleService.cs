using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Core.Time;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class ScheduleService : IScheduleService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;
    private readonly RuleEvaluationEngine _engine = new();

    public ScheduleService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<WideTableDto?> GetCurrentAsync(Unit unit, YearMonth month, CancellationToken ct = default)
    {
        var schedule = await _db.MonthSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Unit == unit && s.Month == month.ToString(), ct);
        return schedule is null ? null : await BuildWideTableAsync(schedule, editable: true, ct);
    }

    public async Task<WideTableDto> GetAsync(long scheduleId, CancellationToken ct = default)
    {
        var schedule = await _db.MonthSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scheduleId, ct)
            ?? throw new KeyNotFoundException($"找不到班表 {scheduleId}");
        return await BuildWideTableAsync(schedule, editable: true, ct);
    }

    public Task<bool> ExistsAsync(Unit unit, YearMonth month, CancellationToken ct = default) =>
        _db.MonthSchedules.AsNoTracking()
            .AnyAsync(s => s.Unit == unit && s.Month == month.ToString(), ct);

    public async Task<long> SelectCandidateAsync(long candidateId, string op, CancellationToken ct = default)
    {
        var candidate = await _db.CandidateSolutions
            .Include(c => c.Run)
            .FirstOrDefaultAsync(c => c.Id == candidateId, ct)
            ?? throw new KeyNotFoundException($"找不到候選 {candidateId}");
        if (candidate.IsShortageAnalysis)
            throw new InvalidOperationException("缺班分析不可選為目前班表");

        var run = candidate.Run ?? await _db.ScheduleRuns.FirstAsync(r => r.Id == candidate.RunId, ct);
        var monthKey = run.TargetMonth;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var existing = await _db.MonthSchedules
            .FirstOrDefaultAsync(s => s.Unit == run.Unit && s.Month == monthKey, ct);
        if (existing is not null)
        {
            await DeleteScheduleDataAsync(existing.Id, ct);
            _db.MonthSchedules.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        var schedule = new MonthSchedule
        {
            Unit = run.Unit,
            Month = monthKey,
            SourceRunId = run.Id,
            SourceCandidateId = candidate.Id,
            UpdatedAt = TaipeiTime.Now,
            Operator = op
        };
        _db.MonthSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);

        var source = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.Candidate && a.OwnerId == candidateId)
            .ToListAsync(ct);
        foreach (var a in source)
        {
            _db.Assignments.Add(new Assignment
            {
                OwnerType = AssignmentOwnerType.Schedule,
                OwnerId = schedule.Id,
                EmployeeId = a.EmployeeId,
                Date = a.Date,
                State = a.State
            });
        }

        _audit.Add(op, "Schedule.SelectCandidate", "MonthSchedule", schedule.Id.ToString(),
            after: new { candidateId, schedule.Id, run.Unit, monthKey });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return schedule.Id;
    }

    public async Task<IReadOnlyList<CellOptionDto>> GetCellOptionsAsync(
        long scheduleId, string employeeId, DateOnly date, CancellationToken ct = default)
    {
        var schedule = await _db.MonthSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scheduleId, ct)
            ?? throw new KeyNotFoundException($"找不到班表 {scheduleId}");

        var ctx = await ScheduleContextBuilder.BuildForScheduleAsync(_db, schedule, ct);
        var emp = ctx.Employees.FirstOrDefault(e => e.Id == employeeId)
            ?? throw new KeyNotFoundException($"找不到員工 {employeeId}");

        var states = new List<DayState>();
        if (schedule.Unit == Unit.M)
        {
            var home = emp.HomeStation ?? throw new InvalidOperationException($"M 員工缺少本站：{employeeId}");
            foreach (var st in StationConfig.StationsInGroup(StationConfig.GroupOf(home)))
            {
                foreach (var shift in StationConfig.ShiftsForStation(st))
                    states.Add(DayState.Work(shift, st == home ? null : st));
            }
        }
        else
        {
            var expected = ctx.MonthlyShifts is not null && ctx.MonthlyShifts.TryGetValue(employeeId, out var s)
                ? s
                : ShiftType.Morning;
            states.Add(DayState.Work(expected));
        }

        states.Add(DayState.Rest);
        states.Add(DayState.RStar);
        states.Add(DayState.HolidayRest);
        states.Add(DayState.X);

        var options = new List<CellOptionDto>();
        foreach (var state in states)
        {
            var trial = WithAssignment(ctx, employeeId, date, state);
            var hard = _engine.EvaluateHard(trial);
            var violations = hard
                .Where(r => r.ViolationCount > 0)
                .Select(r => r.RuleId)
                .ToList();
            options.Add(new CellOptionDto(state, violations));
        }

        return options;
    }

    public async Task<ScheduleValidationDto> ApplyEditAsync(
        long scheduleId, string employeeId, DateOnly date, DayState state, string op,
        CancellationToken ct = default)
    {
        var schedule = await _db.MonthSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct)
            ?? throw new KeyNotFoundException($"找不到班表 {scheduleId}");

        var assignment = await _db.Assignments
            .FirstOrDefaultAsync(a =>
                a.OwnerType == AssignmentOwnerType.Schedule && a.OwnerId == scheduleId
                && a.EmployeeId == employeeId && a.Date == date, ct)
            ?? throw new KeyNotFoundException($"找不到格子：{employeeId} @ {date}");

        var before = assignment.State;
        var after = state.ToDisplay();
        assignment.State = after;
        schedule.UpdatedAt = TaipeiTime.Now;
        schedule.Operator = op;

        var seq = await _db.ScheduleEdits.Where(e => e.ScheduleId == scheduleId)
            .Select(e => (int?)e.Seq).MaxAsync(ct) ?? 0;
        _db.ScheduleEdits.Add(new ScheduleEdit
        {
            ScheduleId = scheduleId,
            Seq = seq + 1,
            EmployeeId = employeeId,
            Date = date,
            BeforeState = before,
            AfterState = after,
            Operator = op,
            At = TaipeiTime.Now
        });
        _audit.Add(op, "Schedule.Edit", "MonthSchedule", scheduleId.ToString(),
            before: new { employeeId, date, before },
            after: new { employeeId, date, after });
        await _db.SaveChangesAsync(ct);
        return await RevalidateAsync(scheduleId, ct);
    }

    public async Task<ScheduleValidationDto> UndoAsync(
        long scheduleId, string op, CancellationToken ct = default)
    {
        var schedule = await _db.MonthSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct)
            ?? throw new KeyNotFoundException($"找不到班表 {scheduleId}");

        var last = await _db.ScheduleEdits
            .Where(e => e.ScheduleId == scheduleId)
            .OrderByDescending(e => e.Seq)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("沒有可復原的修改");

        var assignment = await _db.Assignments.FirstAsync(a =>
            a.OwnerType == AssignmentOwnerType.Schedule && a.OwnerId == scheduleId
            && a.EmployeeId == last.EmployeeId && a.Date == last.Date, ct);
        assignment.State = last.BeforeState;
        schedule.UpdatedAt = TaipeiTime.Now;
        schedule.Operator = op;
        _db.ScheduleEdits.Remove(last);
        _audit.Add(op, "Schedule.Undo", "MonthSchedule", scheduleId.ToString(), before: last);
        await _db.SaveChangesAsync(ct);
        return await RevalidateAsync(scheduleId, ct);
    }

    public async Task<ScheduleValidationDto> RevalidateAsync(long scheduleId, CancellationToken ct = default)
    {
        var schedule = await _db.MonthSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scheduleId, ct)
            ?? throw new KeyNotFoundException($"找不到班表 {scheduleId}");

        var ctx = await ScheduleContextBuilder.BuildForScheduleAsync(_db, schedule, ct);
        var results = _engine.EvaluateAll(ctx);
        var hard = results.Where(r => r.RuleId.Contains("-H-", StringComparison.Ordinal)).ToList();
        var p0Passed = hard.All(r => r.ViolationCount == 0);

        var metrics = results
            .Select(r => new RuleMetricDto(r.RuleId, r.ViolationCount, r.RuleId.Contains("-H-", StringComparison.Ordinal)))
            .ToList();
        var violations = results.SelectMany(r => r.Items).ToList();

        IReadOnlyList<MCoverageRow>? mCov = null;
        IReadOnlyList<TCoverageRow>? tCov = null;
        if (ctx.Unit == Unit.M)
            mCov = CoverageCalculator.ComputeM(ctx);
        else
            tCov = CoverageCalculator.ComputeT(ctx);

        return new ScheduleValidationDto(p0Passed, metrics, mCov, tCov, violations);
    }

    public async Task<long> CreateSnapshotAsync(long scheduleId, string op, CancellationToken ct = default)
    {
        var schedule = await _db.MonthSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scheduleId, ct)
            ?? throw new KeyNotFoundException($"找不到班表 {scheduleId}");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var nextNo = await _db.ScheduleSnapshots
            .Where(v => v.Unit == schedule.Unit && v.Month == schedule.Month)
            .Select(v => (int?)v.VersionNo)
            .MaxAsync(ct) ?? 0;

        // Manual snapshots are not history "current"; leave IsCurrent alone for import snapshots.
        var snap = new ScheduleSnapshot
        {
            Unit = schedule.Unit,
            Month = schedule.Month,
            VersionNo = nextNo + 1,
            CreatedAt = TaipeiTime.Now,
            Operator = op,
            IsCurrent = false
        };
        _db.ScheduleSnapshots.Add(snap);
        await _db.SaveChangesAsync(ct);

        var assignments = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.Schedule && a.OwnerId == scheduleId)
            .ToListAsync(ct);
        foreach (var a in assignments)
        {
            _db.Assignments.Add(new Assignment
            {
                OwnerType = AssignmentOwnerType.Snapshot,
                OwnerId = snap.Id,
                EmployeeId = a.EmployeeId,
                Date = a.Date,
                State = a.State
            });
        }

        _audit.Add(op, "Schedule.Snapshot", "ScheduleSnapshot", snap.Id.ToString(),
            after: new { scheduleId, snap.VersionNo });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return snap.Id;
    }

    public async Task<IReadOnlyList<VersionDto>> GetSnapshotsAsync(
        Unit unit, YearMonth month, CancellationToken ct = default)
    {
        var rows = await _db.ScheduleSnapshots.AsNoTracking()
            .Where(v => v.Unit == unit && v.Month == month.ToString())
            .OrderByDescending(v => v.VersionNo)
            .ToListAsync(ct);
        return rows.Select(v => new VersionDto(
            v.Id, v.Unit, YearMonth.Parse(v.Month), v.VersionNo, v.CreatedAt, v.Operator, v.IsCurrent)).ToList();
    }

    public async Task<WideTableDto> GetSnapshotAsync(long snapshotId, CancellationToken ct = default)
    {
        var snap = await _db.ScheduleSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == snapshotId, ct)
            ?? throw new KeyNotFoundException($"找不到快照 {snapshotId}");

        var month = YearMonth.Parse(snap.Month);
        var assignments = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.Snapshot && a.OwnerId == snapshotId)
            .ToListAsync(ct);
        return await BuildWideTableFromRowsAsync(snap.Unit, month, assignments, editable: false, ownerId: snap.Id, ct);
    }

    public async Task RestoreSnapshotAsync(long snapshotId, string op, CancellationToken ct = default)
    {
        var snap = await _db.ScheduleSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == snapshotId, ct)
            ?? throw new KeyNotFoundException($"找不到快照 {snapshotId}");

        var source = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.Snapshot && a.OwnerId == snapshotId)
            .ToListAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var existing = await _db.MonthSchedules
            .FirstOrDefaultAsync(s => s.Unit == snap.Unit && s.Month == snap.Month, ct);
        if (existing is not null)
        {
            await DeleteScheduleDataAsync(existing.Id, ct);
            _db.MonthSchedules.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        var schedule = new MonthSchedule
        {
            Unit = snap.Unit,
            Month = snap.Month,
            SourceRunId = null,
            SourceCandidateId = null,
            UpdatedAt = TaipeiTime.Now,
            Operator = op
        };
        _db.MonthSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);

        foreach (var a in source)
        {
            _db.Assignments.Add(new Assignment
            {
                OwnerType = AssignmentOwnerType.Schedule,
                OwnerId = schedule.Id,
                EmployeeId = a.EmployeeId,
                Date = a.Date,
                State = a.State
            });
        }

        _audit.Add(op, "Schedule.RestoreSnapshot", "MonthSchedule", schedule.Id.ToString(),
            after: new { snapshotId, schedule.Id });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task DeleteScheduleDataAsync(long scheduleId, CancellationToken ct)
    {
        var assignments = await _db.Assignments
            .Where(a => a.OwnerType == AssignmentOwnerType.Schedule && a.OwnerId == scheduleId)
            .ToListAsync(ct);
        _db.Assignments.RemoveRange(assignments);
        var edits = await _db.ScheduleEdits.Where(e => e.ScheduleId == scheduleId).ToListAsync(ct);
        _db.ScheduleEdits.RemoveRange(edits);
    }

    private async Task<WideTableDto> BuildWideTableAsync(
        MonthSchedule schedule, bool editable, CancellationToken ct)
    {
        var month = YearMonth.Parse(schedule.Month);
        var assignments = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.Schedule && a.OwnerId == schedule.Id)
            .ToListAsync(ct);

        ScheduleValidationDto? validation = null;
        try
        {
            validation = await RevalidateAsync(schedule.Id, ct);
        }
        catch
        {
            // empty / incomplete schedule may still render
        }

        var violationByCell = new Dictionary<(string Emp, DateOnly Date), List<string>>();
        if (validation is not null)
        {
            foreach (var v in validation.Violations)
            {
                if (v.EmployeeId is null || v.Date is null) continue;
                var key = (v.EmployeeId, v.Date.Value);
                if (!violationByCell.TryGetValue(key, out var list))
                {
                    list = [];
                    violationByCell[key] = list;
                }

                if (!list.Contains(v.RuleId))
                    list.Add(v.RuleId);
            }
        }

        return await BuildWideTableFromRowsAsync(
            schedule.Unit, month, assignments, editable, schedule.Id, ct, violationByCell);
    }

    private async Task<WideTableDto> BuildWideTableFromRowsAsync(
        Unit unit,
        YearMonth month,
        List<Assignment> assignments,
        bool editable,
        long ownerId,
        CancellationToken ct,
        IReadOnlyDictionary<(string Emp, DateOnly Date), List<string>>? violationByCell = null)
    {
        var dates = assignments.Select(a => a.Date).Distinct().OrderBy(d => d).ToList();
        if (dates.Count == 0)
            dates = Enumerable.Range(0, month.LastDay.DayNumber - month.FirstDay.DayNumber + 1)
                .Select(i => month.FirstDay.AddDays(i)).ToList();

        var empIds = assignments.Select(a => a.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var assignmentMap = ScheduleContextBuilder.ToAssignmentMap(assignments);
        var ctx = await ScheduleContextBuilder.BuildForAssignmentsAsync(
            _db, unit, month, assignmentMap, ct);

        var rows = new List<WideTableRowDto>();
        foreach (var empId in empIds.OrderBy(x => x))
        {
            employees.TryGetValue(empId, out var emp);
            var cells = new Dictionary<DateOnly, CellDto>();
            foreach (var a in assignments.Where(x => x.EmployeeId == empId))
            {
                IReadOnlyList<string> rules = Array.Empty<string>();
                if (violationByCell is not null
                    && violationByCell.TryGetValue((empId, a.Date), out var list))
                    rules = list;
                cells[a.Date] = new CellDto(
                    a.Date,
                    DayState.ParseDisplay(a.State),
                    a.Date > month.LastDay,
                    editable,
                    rules);
            }

            string? group = null;
            if (emp?.Unit == Unit.M && emp.HomeStation is { } hs && StationConfig.StationGroup.ContainsKey(hs))
                group = StationConfig.GroupOf(hs);

            rows.Add(new WideTableRowDto(
                empId,
                emp?.Name ?? empId,
                emp?.HomeStation,
                group,
                cells,
                RestStatsCalculator.Compute(ctx, empId)));
        }

        return new WideTableDto(unit, month, month.LastDay, dates, rows, IsEditable: editable, OwnerId: ownerId);
    }

    private static ScheduleContext WithAssignment(
        ScheduleContext ctx, string employeeId, DateOnly date, DayState state)
    {
        var assignments = ctx.Assignments.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<DateOnly, DayState>)kv.Value.ToDictionary(d => d.Key, d => d.Value));

        if (!assignments.TryGetValue(employeeId, out var days))
        {
            days = new Dictionary<DateOnly, DayState>();
            assignments[employeeId] = days;
        }

        var mutable = days.ToDictionary(d => d.Key, d => d.Value);
        mutable[date] = state;
        assignments[employeeId] = mutable;

        return new ScheduleContext
        {
            Period = ctx.Period,
            Unit = ctx.Unit,
            Employees = ctx.Employees,
            Cycles = ctx.Cycles,
            Histories = ctx.Histories,
            XEvents = ctx.XEvents,
            Assignments = assignments,
            MonthlyShifts = ctx.MonthlyShifts,
            NextMonthShifts = ctx.NextMonthShifts,
            PreviousMonthShifts = ctx.PreviousMonthShifts,
            RStarRequests = ctx.RStarRequests,
            ExternalSlots = ctx.ExternalSlots
        };
    }
}
