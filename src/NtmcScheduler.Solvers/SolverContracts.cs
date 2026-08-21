namespace NtmcScheduler.Solvers;

/// <summary>Start and end times for one shift. End &lt;= Start means overnight (end is next day).</summary>
public sealed record ShiftTimePair(TimeOnly Start, TimeOnly End);

/// <summary>Early/Afternoon/Night times for one workspace. Null fields fall back to built-in defaults.</summary>
public sealed record WorkspaceShiftTimes(ShiftTimePair Early, ShiftTimePair Afternoon, ShiftTimePair Night)
{
    private static readonly ShiftTimePair MDefaultEarly = new(new TimeOnly(6, 30), new TimeOnly(14, 30));
    private static readonly ShiftTimePair MDefaultAfternoon = new(new TimeOnly(14, 20), new TimeOnly(22, 20));
    private static readonly ShiftTimePair MDefaultNight = new(new TimeOnly(22, 0), new TimeOnly(7, 0));
    private static readonly ShiftTimePair TDefaultEarly = new(new TimeOnly(7, 0), new TimeOnly(15, 0));
    private static readonly ShiftTimePair TDefaultAfternoon = new(new TimeOnly(15, 0), new TimeOnly(23, 0));
    private static readonly ShiftTimePair TDefaultNight = new(new TimeOnly(23, 0), new TimeOnly(7, 0));

    public static readonly WorkspaceShiftTimes DefaultM = new(MDefaultEarly, MDefaultAfternoon, MDefaultNight);
    public static readonly WorkspaceShiftTimes DefaultT = new(TDefaultEarly, TDefaultAfternoon, TDefaultNight);

    public ShiftTimePair For(Shift shift) => shift switch
    {
        Shift.Early => Early,
        Shift.Afternoon => Afternoon,
        Shift.Night => Night,
        _ => throw new ArgumentOutOfRangeException(nameof(shift))
    };

    /// <summary>Resolves shift to a DateTimeOffset interval. End &lt;= Start advances to the next day.</summary>
    public (DateTimeOffset Start, DateTimeOffset End) Resolve(DateOnly date, Shift shift)
    {
        var pair = For(shift);
        var taipeiOffset = TimeSpan.FromHours(8);
        var nextDay = pair.End <= pair.Start;
        return (
            new DateTimeOffset(date.ToDateTime(pair.Start), taipeiOffset),
            new DateTimeOffset(date.AddDays(nextDay ? 1 : 0).ToDateTime(pair.End), taipeiOffset));
    }
}

/// <summary>Standard shift times for both M and T workspaces.</summary>
public sealed record StandardShiftTimes(WorkspaceShiftTimes M, WorkspaceShiftTimes T)
{
    public static readonly StandardShiftTimes Default = new(WorkspaceShiftTimes.DefaultM, WorkspaceShiftTimes.DefaultT);
}

/// <summary>Normal operating shifts. A night shift belongs to its start date.</summary>
public enum Shift
{
    Early,
    Afternoon,
    Night
}

/// <summary>A user-maintained alias that resolves a schedule cell to an X interval.</summary>
public sealed record NonStandardShift(string? Name, TimeOnly StartTime, TimeOnly EndTime, string Code);

/// <summary>The typed boundary for the editable non-standard-shift CSV or future UI.</summary>
public sealed record NonStandardShiftTable(IReadOnlyList<NonStandardShift> Shifts);

/// <summary>The actual assignment stored in a schedule cell.</summary>
public enum AssignmentKind
{
    Work,
    Rest,        // R
    SpecialRest, // R1
    LeaveRest,   // R休
    WorkEvent    // X
}

public enum SolveStatus
{
    Optimal,
    TimeLimit,
    Infeasible,
    InvalidInput
}

public sealed record SolverOptions
{
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromMinutes(5);
    public int RandomSeed { get; init; }
    public int WorkerCount { get; init; } = 8;
    public Dictionary<string, int>? RuleWeights { get; init; }
}

public sealed record InputError(string Field, string Message);

/// <summary>One typed day cell. A missing dictionary entry is an undecided cell.</summary>
public sealed record ScheduleCell
{
    /// <summary>Null with RequestedRest=true represents an unresolved R* request.</summary>
    public AssignmentKind? Kind { get; init; }
    public bool RequestedRest { get; init; }
    public string? Station { get; init; }
    public Shift? Shift { get; init; }
    public DateTimeOffset? EventStart { get; init; }
    public DateTimeOffset? EventEnd { get; init; }
    public string? EventDescription { get; init; }
}

/// <summary>R and R1 already counted against one eight-week interval.</summary>
public sealed record RestUsage(int Rest, int SpecialRest);

/// <summary>One employee's data and assignments for a calendar month.</summary>
public sealed record EmployeeMonthlySchedule
{
    public required string EmployeeId { get; init; }
    public required string Name { get; init; }

    /// <summary>Home station for M; professional group for T.</summary>
    public required string Affiliation { get; init; }

    public DateOnly? EmploymentStartDate { get; init; }

    /// <summary>Required for T (1-5); null for M.</summary>
    public int? Ability { get; init; }

    /// <summary>Required for T; null for M.</summary>
    public Shift? MonthlyShift { get; init; }

    /// <summary>M eight-week template identifier; null for T.</summary>
    public string? PerpetualScheduleId { get; init; }

    /// <summary>Maximum target-month R休 count. Null means zero; solved and historical schedules leave it null.</summary>
    public int? RequestedLeaveRestCount { get; init; }

    public RestUsage? OpeningUsage { get; init; }
    public required IReadOnlyDictionary<DateOnly, ScheduleCell> Assignments { get; init; }
    public RestUsage? ClosingUsage { get; init; }
    public int? NormalWorkCount { get; init; }
}

/// <summary>A typed representation of one monthly schedule CSV/worksheet.</summary>
public sealed record MonthlySchedule(
    DateOnly MonthStart,
    IReadOnlyList<EmployeeMonthlySchedule> Employees);

/// <summary>An inclusive 56-day interval. R1 quota equals NationalHolidays.Count.</summary>
public sealed record RestInterval(
    DateOnly Start,
    DateOnly End,
    HashSet<DateOnly> NationalHolidays);

public enum ExternalSupportLevel
{
    Disallowed,
    Discouraged,
    Allowed
}

public sealed record StaffingRange(int Minimum, int Maximum);

public sealed record MStationSetting(
    string Code,
    string Group,
    ExternalSupportLevel ExternalSupport,
    StaffingRange Early,
    StaffingRange Afternoon,
    StaffingRange Night)
{
    public StaffingRange For(Shift shift) => shift switch
    {
        Shift.Early => Early,
        Shift.Afternoon => Afternoon,
        Shift.Night => Night,
        _ => throw new ArgumentOutOfRangeException(nameof(shift))
    };
}

public sealed record MonthlySchedulingSettings(
    int GeneralRestTarget,
    int SpecialRestTarget,
    MStationSetting[] MStations);

public static class MonthlySchedulingDefaults
{
    public static MonthlySchedulingSettings Create(DateOnly month, IReadOnlyList<RestInterval> intervals, int employeeCount)
    {
        var dates = Enumerable.Range(0, DateTime.DaysInMonth(month.Year, month.Month)).Select(month.AddDays).ToArray();
        var holidays = intervals.SelectMany(x => x.NationalHolidays).ToHashSet();
        var stations = Enumerable.Range(1, 12).Select(number =>
        {
            var code = $"LB{number:D2}";
            var maximum = code is "LB01" or "LB06" or "LB07" or "LB12" ? Math.Max(1, employeeCount) : 1;
            var external = code is "LB02" or "LB04" or "LB11" ? ExternalSupportLevel.Allowed
                : code == "LB09" ? ExternalSupportLevel.Discouraged : ExternalSupportLevel.Disallowed;
            return new MStationSetting(code, $"G{(number - 1) / 3 + 1}", external,
                new(1, maximum), new(1, maximum), new(code is "LB01" or "LB06" or "LB08" or "LB12" ? 1 : 0,
                    code is "LB01" or "LB06" or "LB08" or "LB12" ? 1 : 0));
        }).ToArray();
        return new(dates.Count(x => x.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday), dates.Count(holidays.Contains), stations);
    }
}

public static class SolverRuleWeights
{
    public static readonly IReadOnlyDictionary<string, int> M = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["RequestedRest"] = 3, ["UnusedLeaveRest"] = 1, ["ExternalStaffing"] = 5,
        ["MonthlyRest"] = 240, ["SpecialRestBalance"] = 120, ["WorkStreak"] = 20,
        ["MixedShiftWorkStreak"] = 15, ["NightRestEarly"] = 400, ["NightRestAfternoon"] = 300,
        ["ShiftChangeWithoutRest"] = 5, ["HolidayRestFairness"] = 5,
        ["EarlyAfternoonImbalance"] = 20, ["NightShiftTarget"] = 50
    };

    public static readonly IReadOnlyDictionary<string, int> T = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["RequestedRest"] = 3, ["UnusedLeaveRest"] = 1, ["NonMonthlyShift"] = 9,
        ["Attendance"] = 9, ["Specialty"] = 3, ["Ability"] = 1, ["MonthlyRest"] = 1,
        ["SpecialRestBalance"] = 1, ["WorkStreak"] = 3, ["NightToEarlyRest"] = 12,
        ["MonthBoundaryRestBalance"] = 5, ["WeekdayRestFairness"] = 2, ["HolidayRestFairness"] = 4
    };

    public static IReadOnlyDictionary<string, int> Resolve(bool isM, Dictionary<string, int>? requested)
    {
        var defaults = isM ? M : T;
        if (requested is null) return defaults;
        if (requested.Count != defaults.Count || requested.Any(pair => pair.Value < 0 || !defaults.ContainsKey(pair.Key)))
            throw new ArgumentException("Rule weights must contain every active rule exactly once and be non-negative.", nameof(requested));
        return requested;
    }
}

/// <summary>The complete input shared by the independent M and T solvers.</summary>
public sealed record ScheduleInput(
    MonthlySchedule PreviousMonth,
    MonthlySchedule DemandMonth,
    IReadOnlyList<RestInterval> RestIntervals,
    NonStandardShiftTable NonStandardShifts,
    StandardShiftTimes? StandardShiftTimes = null,
    MonthlySchedulingSettings? MonthlySettings = null);

public sealed record ObjectiveComponent(string Name, long Value, int Weight)
{
    public long WeightedValue => Value * Weight;
}

/// <summary>A named lexicographic objective group and its component breakdown.</summary>
public sealed record ObjectiveScore(
    int Priority,
    string Name,
    long Value,
    IReadOnlyList<ObjectiveComponent> Components);
