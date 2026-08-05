using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation;

public sealed class ScheduleContext
{
    public required SchedulePeriod Period { get; init; }
    public required Unit Unit { get; init; }
    public required IReadOnlyList<EmployeeInfo> Employees { get; init; }
    public required IReadOnlyList<CycleInfo> Cycles { get; init; }
    public required IReadOnlyDictionary<string, EmployeeHistory> Histories { get; init; }
    public required IReadOnlyList<XEvent> XEvents { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> Assignments { get; init; }
    public IReadOnlyDictionary<string, ShiftType>? MonthlyShifts { get; init; }
    public IReadOnlyDictionary<string, ShiftType>? NextMonthShifts { get; init; }
    public IReadOnlyList<(string EmployeeId, DateOnly Date)> RStarRequests { get; init; } =
        Array.Empty<(string, DateOnly)>();

    /// <summary>
    /// M external (外派) slot fills: (station, date, shift). No fake employee rows.
    /// </summary>
    public IReadOnlySet<(string Station, DateOnly Date, ShiftType Shift)> ExternalSlots { get; init; } =
        new HashSet<(string, DateOnly, ShiftType)>();

    /// <summary>
    /// Previous-month shift group for T (used by T-S-MONTH-REST / T-S-MONTH-BALANCE).
    /// </summary>
    public IReadOnlyDictionary<string, ShiftType>? PreviousMonthShifts { get; init; }

    public DayState? GetState(string employeeId, DateOnly date)
    {
        if (Assignments.TryGetValue(employeeId, out var days) && days.TryGetValue(date, out var state))
            return state;
        if (Histories.TryGetValue(employeeId, out var hist) && hist.Days.TryGetValue(date, out var h))
            return h;
        return null;
    }

    public DayState RequireState(string employeeId, DateOnly date) =>
        GetState(employeeId, date)
        ?? throw new InvalidOperationException($"缺少指派：{employeeId} @ {date:yyyy-MM-dd}");

    public EmployeeInfo RequireEmployee(string employeeId) =>
        Employees.FirstOrDefault(e => e.Id == employeeId)
        ?? throw new InvalidOperationException($"未知員工：{employeeId}");
}
