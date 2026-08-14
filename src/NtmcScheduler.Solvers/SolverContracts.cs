namespace NtmcScheduler.Solvers;

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
    IReadOnlySet<DateOnly> NationalHolidays);

/// <summary>The complete input shared by the independent M and T solvers.</summary>
public sealed record ScheduleInput(
    MonthlySchedule PreviousMonth,
    MonthlySchedule DemandMonth,
    IReadOnlyList<RestInterval> RestIntervals,
    NonStandardShiftTable NonStandardShifts);

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
