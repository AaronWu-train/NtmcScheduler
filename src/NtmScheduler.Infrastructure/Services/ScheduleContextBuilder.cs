using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;
using FixedEventType = NtmScheduler.Core.Abstractions.Dtos.FixedEventType;

namespace NtmScheduler.Infrastructure.Services;

/// <summary>
/// Builds ScheduleContext / EmployeeHistory from DB for validation and solver warm-start.
/// Prefers MonthSchedule assignments; falls back to IsCurrent ScheduleSnapshot (history import).
/// </summary>
internal static class ScheduleContextBuilder
{
    public static async Task<ScheduleContext> BuildForScheduleAsync(
        NtmDbContext db, MonthSchedule schedule, CancellationToken ct)
    {
        var month = YearMonth.Parse(schedule.Month);
        var assignmentRows = await db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.Schedule && a.OwnerId == schedule.Id)
            .ToListAsync(ct);
        return await BuildForAssignmentsAsync(db, schedule.Unit, month, ToAssignmentMap(assignmentRows), ct);
    }

    public static async Task<ScheduleContext> BuildForAssignmentsAsync(
        NtmDbContext db,
        Unit unit,
        YearMonth month,
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> assignments,
        CancellationToken ct)
    {
        var period = ScheduleCalendar.CreatePeriod(month);
        var employees = await LoadEmployeesAsync(db, unit, ct);
        var empIds = employees.Select(e => e.Id).ToList();
        var cycles = await LoadCyclesAsync(db, period, ct);
        var xEvents = await LoadXEventsAsync(db, empIds, ct);
        var rStars = await LoadRStarsAsync(db, empIds, ct);
        var monthly = unit == Unit.T
            ? await LoadMonthlyShiftsAsync(db, month, ct)
            : null;

        var histStart = cycles.Count > 0
            ? cycles.Min(c => c.Start)
            : period.FirstDay.AddDays(-56);
        var histories = await LoadHistoriesAsync(db, unit, empIds, histStart, period.FirstDay, ct);

        return new ScheduleContext
        {
            Period = period,
            Unit = unit,
            Employees = employees,
            Cycles = cycles,
            Histories = histories,
            XEvents = xEvents,
            Assignments = assignments,
            MonthlyShifts = monthly,
            RStarRequests = rStars
        };
    }

    public static async Task<IReadOnlyDictionary<string, EmployeeHistory>> LoadHistoriesAsync(
        NtmDbContext db,
        Unit unit,
        IReadOnlyList<string> empIds,
        DateOnly rangeStart,
        DateOnly exclusiveEnd,
        CancellationToken ct)
    {
        if (empIds.Count == 0 || rangeStart >= exclusiveEnd)
            return new Dictionary<string, EmployeeHistory>();

        var dayMap = await LoadDayStatesInRangeAsync(db, unit, empIds, rangeStart, exclusiveEnd, ct);
        var result = new Dictionary<string, EmployeeHistory>();
        foreach (var empId in empIds)
        {
            dayMap.TryGetValue(empId, out var days);
            days ??= new Dictionary<DateOnly, DayState>();
            result[empId] = BuildHistory(unit, days, rangeStart, exclusiveEnd);
        }

        return result;
    }

    public static EmployeeHistory BuildHistory(
        Unit unit,
        IReadOnlyDictionary<DateOnly, DayState> days,
        DateOnly rangeStart,
        DateOnly exclusiveEnd)
    {
        DateTime? lastEnd = null;
        for (var d = exclusiveEnd.AddDays(-1); d >= rangeStart; d = d.AddDays(-1))
        {
            if (days.TryGetValue(d, out var st) && st.IsNormalShift)
            {
                lastEnd = ShiftTimeConfig.Interval(unit, d, st.Shift!.Value).End;
                break;
            }
        }

        var open = ComputeOpenBlock(days, rangeStart, exclusiveEnd.AddDays(-1));
        return new EmployeeHistory(days, lastEnd, open);
    }

    private static (ShiftType Shift, int Count)? ComputeOpenBlock(
        IReadOnlyDictionary<DateOnly, DayState> days, DateOnly rangeStart, DateOnly lastDay)
    {
        ShiftType? current = null;
        var count = 0;
        for (var d = lastDay; d >= rangeStart; d = d.AddDays(-1))
        {
            if (!days.TryGetValue(d, out var st))
                break;
            if (st.Type == DayStateType.X || st.IsAnyRest)
                continue;
            if (!st.IsNormalShift)
                break;
            var shift = st.Shift!.Value;
            if (current is null)
            {
                current = shift;
                count = 1;
            }
            else if (current == shift)
            {
                count++;
            }
            else
            {
                break;
            }
        }

        return current is null ? null : (current.Value, count);
    }

    /// <summary>
    /// Load day states for [rangeStart, exclusiveEnd) preferring MonthSchedule, else current Snapshot.
    /// </summary>
    public static async Task<Dictionary<string, Dictionary<DateOnly, DayState>>> LoadDayStatesInRangeAsync(
        NtmDbContext db,
        Unit unit,
        IReadOnlyList<string> empIds,
        DateOnly rangeStart,
        DateOnly exclusiveEnd,
        CancellationToken ct)
    {
        var result = empIds.ToDictionary(id => id, _ => new Dictionary<DateOnly, DayState>());
        if (empIds.Count == 0)
            return result;

        // Collect owner ids month by month.
        var cursor = new YearMonth(rangeStart.Year, rangeStart.Month);
        var endMonth = new YearMonth(exclusiveEnd.AddDays(-1).Year, exclusiveEnd.AddDays(-1).Month);
        var ownerPairs = new List<(AssignmentOwnerType Type, long Id)>();

        while (cursor.CompareTo(endMonth) <= 0)
        {
            var monthKey = cursor.ToString();
            var schedule = await db.MonthSchedules.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Unit == unit && s.Month == monthKey, ct);
            if (schedule is not null)
            {
                ownerPairs.Add((AssignmentOwnerType.Schedule, schedule.Id));
            }
            else
            {
                var snap = await db.ScheduleSnapshots.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Unit == unit && s.Month == monthKey && s.IsCurrent, ct);
                if (snap is not null)
                    ownerPairs.Add((AssignmentOwnerType.Snapshot, snap.Id));
            }

            cursor = cursor.Next();
        }

        if (ownerPairs.Count == 0)
            return result;

        // Load all candidate owners then filter in memory (OwnerType is enum string).
        foreach (var group in ownerPairs.GroupBy(p => p.Type))
        {
            var ids = group.Select(g => g.Id).ToList();
            var rows = await db.Assignments.AsNoTracking()
                .Where(a => a.OwnerType == group.Key && ids.Contains(a.OwnerId)
                            && empIds.Contains(a.EmployeeId)
                            && a.Date >= rangeStart && a.Date < exclusiveEnd)
                .ToListAsync(ct);
            foreach (var a in rows)
            {
                if (!result.TryGetValue(a.EmployeeId, out var days))
                    continue;
                // Prefer Schedule over Snapshot: if already set from Schedule, skip Snapshot overwrite.
                // We load Schedule owners first in ownerPairs order within each month, so first write wins
                // only if we process Schedule before Snapshot — which we do per month.
                if (!days.ContainsKey(a.Date))
                    days[a.Date] = DayState.ParseDisplay(a.State);
            }
        }

        return result;
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> ToAssignmentMap(
        IEnumerable<Assignment> rows)
    {
        var map = new Dictionary<string, Dictionary<DateOnly, DayState>>();
        foreach (var a in rows)
        {
            if (!map.TryGetValue(a.EmployeeId, out var days))
            {
                days = new Dictionary<DateOnly, DayState>();
                map[a.EmployeeId] = days;
            }

            days[a.Date] = DayState.ParseDisplay(a.State);
        }

        return map.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<DateOnly, DayState>)kv.Value);
    }

    public static async Task<IReadOnlyList<EmployeeInfo>> LoadEmployeesAsync(
        NtmDbContext db, Unit unit, CancellationToken ct) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.Unit == unit)
            .OrderBy(e => e.Id)
            .Select(e => new EmployeeInfo(e.Id, e.Name, e.Unit, e.HomeStation, e.Specialty, e.Ability))
            .ToListAsync(ct);

    public static async Task<IReadOnlyList<CycleInfo>> LoadCyclesAsync(
        NtmDbContext db, SchedulePeriod period, CancellationToken ct) =>
        await db.ScheduleCycles.AsNoTracking()
            .Where(c => c.Start <= period.RangeEnd && c.End >= period.FirstDay)
            .Select(c => new CycleInfo(c.Start, c.End, c.RequiredR, c.RequiredR1))
            .ToListAsync(ct);

    public static async Task<IReadOnlyList<XEvent>> LoadXEventsAsync(
        NtmDbContext db, IReadOnlyList<string> empIds, CancellationToken ct) =>
        await db.FixedEvents.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId) && e.Type == FixedEventType.X
                        && e.Start != null && e.End != null)
            .Select(e => new XEvent(e.EmployeeId, e.Start!.Value, e.End!.Value, e.Description ?? ""))
            .ToListAsync(ct);

    public static async Task<IReadOnlyList<(string EmployeeId, DateOnly Date)>> LoadRStarsAsync(
        NtmDbContext db, IReadOnlyList<string> empIds, CancellationToken ct)
    {
        var rows = await db.FixedEvents.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId) && e.Type == FixedEventType.RStar && e.Date != null)
            .Select(e => new { e.EmployeeId, Date = e.Date!.Value })
            .ToListAsync(ct);
        return rows.Select(e => (e.EmployeeId, e.Date)).ToList();
    }

    public static async Task<IReadOnlyDictionary<string, ShiftType>> LoadMonthlyShiftsAsync(
        NtmDbContext db, YearMonth month, CancellationToken ct) =>
        await db.EmployeeMonthlyShifts.AsNoTracking()
            .Where(s => s.Month == month.ToString())
            .ToDictionaryAsync(s => s.EmployeeId, s => s.Shift, ct);
}
