using Google.OrTools.Sat;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Solvers.Common;

/// <summary>
/// Per-employee-day decision variables (rest and r1 are separate).
/// </summary>
public sealed class EmployeeDayVars
{
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public BoolVar? Rest { get; init; }
    public BoolVar? R1 { get; init; }
    public Dictionary<(string Station, ShiftType Shift), BoolVar> Work { get; } = new();
    public Dictionary<ShiftType, BoolVar> ShiftOr { get; } = new();
    public BoolVar? WorkAny { get; set; }
    public bool IsFixed { get; init; }
    public DayState? FixedState { get; init; }
}

public sealed class BuiltModel
{
    public required CpModel Model { get; init; }
    public required Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> Days { get; init; }
    public Dictionary<(string Station, DateOnly Date, ShiftType Shift), BoolVar> Ext { get; } = new();
    public Dictionary<(string Station, DateOnly Date, ShiftType Shift), BoolVar> Slack { get; } = new();
    public Dictionary<string, IntVar> SoftObjectives { get; } = new();
    public required SolveRequestView Request { get; init; }
}

/// <summary>Solver-facing view of <see cref="NtmScheduler.Core.Abstractions.Dtos.SolveRequest"/>.</summary>
public sealed class SolveRequestView
{
    public required NtmScheduler.Core.Abstractions.Dtos.SolveRequest Request { get; init; }

    public Unit Unit => Request.Unit;
    public SchedulePeriod Period => Request.Period;
    public IReadOnlyList<EmployeeInfo> Employees => Request.Employees;
    public IReadOnlyList<CycleInfo> Cycles => Request.Cycles;
    public IReadOnlyDictionary<string, EmployeeHistory> Histories => Request.Histories;
    public IReadOnlyList<XEvent> XEvents => Request.XEvents;
    public IReadOnlyList<(string EmployeeId, DateOnly Date)> RStarRequests => Request.RStarRequests;
    public IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> FixedAssignments =>
        Request.FixedAssignments;

    public DayState? GetFixed(string empId, DateOnly date)
    {
        if (FixedAssignments.TryGetValue(empId, out var map) && map.TryGetValue(date, out var s))
            return s;
        if (date < Period.FirstDay && Histories.TryGetValue(empId, out var h) &&
            h.Days.TryGetValue(date, out var hs))
            return hs;
        return null;
    }

    public bool IsRStarRequest(string empId, DateOnly date) =>
        RStarRequests.Any(r => r.EmployeeId == empId && r.Date == date);

    public ShiftType ResolveTShift(string empId, DateOnly date)
    {
        if (Period.IsInTargetMonth(date))
        {
            if (Request.MonthlyShifts is not null && Request.MonthlyShifts.TryGetValue(empId, out var s))
                return s;
            throw new InvalidOperationException($"缺少 T 月班組：{empId}");
        }

        if (Request.NextMonthShifts is not null && Request.NextMonthShifts.TryGetValue(empId, out var next))
            return next;
        if (Request.MonthlyShifts is not null && Request.MonthlyShifts.TryGetValue(empId, out var cur))
            return cur.NextInRotation();
        throw new InvalidOperationException($"無法解析延伸日班別：{empId} @ {date:yyyy-MM-dd}");
    }
}
