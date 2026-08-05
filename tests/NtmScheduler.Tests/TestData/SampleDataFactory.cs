using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.SampleData;

namespace NtmScheduler.Tests.TestData;

/// <summary>
/// Thin test façade over <see cref="DemoDataset"/> (fixed seed, requiredR=16).
/// </summary>
public static class SampleDataFactory
{
    public const int Seed = DemoDataset.Seed;

    public static IReadOnlyList<EmployeeInfo> CreateMEmployees(int? uniformPerStation = null) =>
        DemoDataset.CreateMEmployees(uniformPerStation);

    public static IReadOnlyList<EmployeeInfo> CreateTEmployees(int perShift = 10) =>
        DemoDataset.CreateTEmployees(perShift);

    public static IReadOnlyDictionary<string, ShiftType> CreateTMonthlyShifts(
        IReadOnlyList<EmployeeInfo> employees,
        YearMonth month,
        ShiftType baseShiftForFirstTen = ShiftType.Morning) =>
        DemoDataset.CreateTMonthlyShifts(employees, baseShiftForFirstTen);

    /// <summary>
    /// Build 2026 8-week cycles starting from the Monday of 2025-12-29.
    /// requiredR1 is 0 or 2 alternating for sample coverage.
    /// </summary>
    public static IReadOnlyList<CycleInfo> Create2026Cycles() =>
        DemoDataset.Create2026Cycles();

    public static SchedulePeriod Period(string yearMonth) =>
        ScheduleCalendar.CreatePeriod(YearMonth.Parse(yearMonth));

    public static DemoBundle BuildDemo(YearMonth? targetMonth = null) =>
        DemoDataset.Build(targetMonth);
}
