using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Tests.TestData;

/// <summary>
/// Deterministic sample data (fixed seed): M 48 / T 30, 2026 cycles with requiredR=16.
/// </summary>
public static class SampleDataFactory
{
    public const int Seed = 20260805;

    public static IReadOnlyList<EmployeeInfo> CreateMEmployees(int perStation = 4)
    {
        var list = new List<EmployeeInfo>();
        var n = 1;
        foreach (var station in StationConfig.AllStations)
        {
            for (var i = 0; i < perStation; i++)
            {
                list.Add(new EmployeeInfo($"M{n:D3}", $"站務{n:D3}", Unit.M, HomeStation: station));
                n++;
            }
        }
        return list;
    }

    public static IReadOnlyList<EmployeeInfo> CreateTEmployees(int perShift = 10)
    {
        var specialties = new[] { "軌道", "號誌", "電力", null };
        var list = new List<EmployeeInfo>();
        var n = 1;
        var rng = new Random(Seed);
        foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
        {
            for (var i = 0; i < perShift; i++)
            {
                list.Add(new EmployeeInfo(
                    $"T{n:D3}",
                    $"檢測{n:D3}",
                    Unit.T,
                    Specialty: specialties[rng.Next(specialties.Length)],
                    Ability: rng.Next(1, 6)));
                n++;
            }
        }
        return list;
    }

    public static IReadOnlyDictionary<string, ShiftType> CreateTMonthlyShifts(
        IReadOnlyList<EmployeeInfo> employees,
        YearMonth month,
        ShiftType baseShiftForFirstTen = ShiftType.Night)
    {
        var map = new Dictionary<string, ShiftType>();
        var order = new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night };
        var baseIdx = Array.IndexOf(order, baseShiftForFirstTen);
        var i = 0;
        foreach (var e in employees.Where(x => x.Unit == Unit.T))
        {
            var group = i / 10;
            map[e.Id] = order[(baseIdx + group) % 3];
            i++;
        }
        return map;
    }

    /// <summary>
    /// Build 2026 8-week cycles starting from the Monday of 2025-12-29.
    /// requiredR1 is 0 or 2 alternating for sample coverage.
    /// </summary>
    public static IReadOnlyList<CycleInfo> Create2026Cycles()
    {
        var cycles = new List<CycleInfo>();
        var start = new DateOnly(2025, 12, 29); // Monday
        var idx = 0;
        while (start.Year <= 2026)
        {
            var end = start.AddDays(55);
            var r1 = idx % 3 == 1 ? 2 : 0;
            cycles.Add(new CycleInfo(start, end, RequiredR: 16, RequiredR1: r1));
            start = end.AddDays(1);
            idx++;
            if (start.Year > 2026 && start.Month > 1) break;
        }
        return cycles;
    }

    public static SchedulePeriod Period(string yearMonth) =>
        ScheduleCalendar.CreatePeriod(YearMonth.Parse(yearMonth));
}
