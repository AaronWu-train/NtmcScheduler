using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.SampleData;

/// <summary>
/// Deterministic demo dataset (fixed seed): M ~43 / T 30, 2026 cycles, history + R*/X.
/// Shared by DemoDataSeeder and tests via SampleDataFactory.
/// </summary>
public static class DemoDataset
{
    public const int Seed = 20260805;
    public static readonly YearMonth DefaultTargetMonth = YearMonth.Parse("2026-08");

    public static IReadOnlyList<EmployeeInfo> CreateMEmployees(int? uniformPerStation = null)
    {
        var list = new List<EmployeeInfo>();
        var n = 1;
        foreach (var station in StationConfig.AllStations)
        {
            var count = uniformPerStation ?? StaffCountFor(station);
            for (var i = 0; i < count; i++)
            {
                list.Add(new EmployeeInfo($"M{n:D3}", $"站務{n:D3}", Unit.M, HomeStation: station));
                n++;
            }
        }
        return list;
    }

    /// <summary>
    /// ~3/station; night stations 4; C-group (no external) a bit more for solvability.
    /// </summary>
    public static int StaffCountFor(string station)
    {
        var night = StationConfig.NightStations.Contains(station);
        var group = StationConfig.GroupOf(station);
        if (group == "C")
            return night ? 5 : 4;
        return night ? 4 : 3;
    }

    public static IReadOnlyList<EmployeeInfo> CreateTEmployees(int perShift = 10)
    {
        var specialties = new[] { "軌道", "號誌", "機電", null };
        var list = new List<EmployeeInfo>();
        var n = 1;
        var rng = new Random(Seed);
        foreach (var _ in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
        {
            for (var i = 0; i < perShift; i++)
            {
                // Guarantee specialty coverage in each shift group (first three slots).
                var specialty = i < 3 ? specialties[i] : specialties[rng.Next(specialties.Length)];
                // Bias ability toward ≥3 so T-S-ABILITY averages are testable.
                var ability = i < 3 ? 3 + (i % 3) : rng.Next(2, 6);
                list.Add(new EmployeeInfo(
                    $"T{n:D3}",
                    $"檢測{n:D3}",
                    Unit.T,
                    Specialty: specialty,
                    Ability: ability));
                n++;
            }
        }
        return list;
    }

    public static IReadOnlyDictionary<string, ShiftType> CreateTMonthlyShifts(
        IReadOnlyList<EmployeeInfo> employees,
        ShiftType baseShiftForFirstTen = ShiftType.Morning)
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
    /// requiredR=16; requiredR1 is 0 or 2 alternating for sample coverage.
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

    public static DemoBundle Build(YearMonth? targetMonth = null)
    {
        var month = targetMonth ?? DefaultTargetMonth;
        var period = ScheduleCalendar.CreatePeriod(month);
        var cycles = Create2026Cycles();
        var mEmps = CreateMEmployees();
        var tEmps = CreateTEmployees();
        var employees = mEmps.Concat(tEmps).ToList();
        var monthly = CreateTMonthlyShifts(tEmps);

        var histFrom = CycleResolver.EarliestIntersectingStart(cycles, period);
        var histTo = period.FirstDay.AddDays(-1);

        var events = new List<DemoFixedEvent>();
        var history = new Dictionary<string, Dictionary<DateOnly, DayState>>();

        foreach (var emp in mEmps)
        {
            var days = BuildMHistoryDays(emp, histFrom, histTo, events);
            history[emp.Id] = days;
        }

        foreach (var emp in tEmps)
        {
            var shift = monthly[emp.Id];
            var days = BuildTHistoryDays(emp, shift, histFrom, histTo, events);
            history[emp.Id] = days;
        }

        // Target-month R* / X (solver inputs); history already embeds some R*/X.
        AddTargetMonthEvents(mEmps, tEmps, month, events);

        // Group history days by calendar month for ScheduleSnapshot rows.
        var byUnitMonth = new Dictionary<(Unit Unit, string Month), Dictionary<string, Dictionary<DateOnly, DayState>>>();
        foreach (var emp in employees)
        {
            if (!history.TryGetValue(emp.Id, out var days))
                continue;
            foreach (var (date, state) in days)
            {
                var key = (emp.Unit, $"{date.Year:D4}-{date.Month:D2}");
                if (!byUnitMonth.TryGetValue(key, out var empMap))
                {
                    empMap = new Dictionary<string, Dictionary<DateOnly, DayState>>();
                    byUnitMonth[key] = empMap;
                }
                if (!empMap.TryGetValue(emp.Id, out var dayMap))
                {
                    dayMap = new Dictionary<DateOnly, DayState>();
                    empMap[emp.Id] = dayMap;
                }
                dayMap[date] = state;
            }
        }

        var historyVersions = byUnitMonth.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>>)
                kv.Value.ToDictionary(
                    e => e.Key,
                    e => (IReadOnlyDictionary<DateOnly, DayState>)e.Value));

        var monthlyByMonth = new Dictionary<(string EmpId, string Month), ShiftType>();
        var monthStr = month.ToString();
        var nextStr = month.Next().ToString();
        foreach (var (id, shift) in monthly)
        {
            monthlyByMonth[(id, monthStr)] = shift;
            monthlyByMonth[(id, nextStr)] = shift.NextInRotation();
        }

        return new DemoBundle(employees, cycles, monthlyByMonth, events, historyVersions, histFrom, histTo, month);
    }

    private static Dictionary<DateOnly, DayState> BuildMHistoryDays(
        EmployeeInfo emp,
        DateOnly from,
        DateOnly to,
        List<DemoFixedEvent> events)
    {
        var days = new Dictionary<DateOnly, DayState>();
        var station = emp.HomeStation!;
        var shifts = StationConfig.ShiftsForStation(station).ToArray();
        var workRun = 0;
        var lastWasNight = false;
        var shiftIdx = Math.Abs(emp.Id.GetHashCode()) % shifts.Length;
        var empNum = int.Parse(emp.Id[1..]);

        // Deterministic special days for a few employees.
        var rStarDay = from.AddDays(3 + empNum % 7);
        var r1Day = from.AddDays(10 + empNum % 5);
        DateOnly? sameDayX = empNum == 1 ? from.AddDays(5) : null;
        DateOnly? overnightX = empNum == 2 ? from.AddDays(12) : null;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (sameDayX == d)
            {
                days[d] = DayState.X;
                events.Add(new DemoFixedEvent(
                    emp.Id, FixedEventType.X, null,
                    d.ToDateTime(new TimeOnly(9, 0)),
                    d.ToDateTime(new TimeOnly(12, 0)),
                    "示範同日 X"));
                workRun++;
                lastWasNight = false;
                continue;
            }

            if (overnightX == d)
            {
                days[d] = DayState.X;
                events.Add(new DemoFixedEvent(
                    emp.Id, FixedEventType.X, null,
                    d.ToDateTime(new TimeOnly(22, 0)),
                    d.AddDays(1).ToDateTime(new TimeOnly(2, 0)),
                    "示範跨午夜 X"));
                workRun++;
                lastWasNight = true; // ends early morning → next day must rest
                continue;
            }

            if (d == rStarDay && d <= to)
            {
                days[d] = DayState.RStar;
                events.Add(new DemoFixedEvent(emp.Id, FixedEventType.RStar, d, null, null, "示範歷史 R*"));
                workRun = 0;
                lastWasNight = false;
                continue;
            }

            if (d == r1Day && d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days[d] = DayState.HolidayRest;
                workRun = 0;
                lastWasNight = false;
                continue;
            }

            // After night / overnight X, force rest (GEN-H-03).
            if (lastWasNight || workRun >= 5 || d.DayOfWeek is DayOfWeek.Sunday)
            {
                days[d] = DayState.Rest;
                workRun = 0;
                lastWasNight = false;
                continue;
            }

            var shift = shifts[shiftIdx % shifts.Length];
            shiftIdx++;
            // Prefer home; occasional cross-station for non-C (LB02/LB04/LB11).
            var workStation = station;
            if (StationConfig.GroupOf(station) != "C"
                && empNum % 11 == 0
                && d.DayOfWeek == DayOfWeek.Tuesday
                && shift != ShiftType.Night)
            {
                var externals = new[] { "LB02", "LB04", "LB11" };
                workStation = externals[empNum % externals.Length];
            }

            days[d] = DayState.Work(shift, workStation);
            workRun++;
            lastWasNight = shift == ShiftType.Night;
        }

        return days;
    }

    private static Dictionary<DateOnly, DayState> BuildTHistoryDays(
        EmployeeInfo emp,
        ShiftType monthlyShift,
        DateOnly from,
        DateOnly to,
        List<DemoFixedEvent> events)
    {
        var days = new Dictionary<DateOnly, DayState>();
        var workRun = 0;
        var lastWasNight = false;
        var empNum = int.Parse(emp.Id[1..]);
        var rStarDay = from.AddDays(4 + empNum % 6);
        DateOnly? sameDayX = empNum == 1 ? from.AddDays(8) : null;
        DateOnly? overnightX = empNum == 5 ? from.AddDays(15) : null;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (sameDayX == d)
            {
                days[d] = DayState.X;
                events.Add(new DemoFixedEvent(
                    emp.Id, FixedEventType.X, null,
                    d.ToDateTime(new TimeOnly(10, 0)),
                    d.ToDateTime(new TimeOnly(14, 0)),
                    "示範同日 X"));
                workRun++;
                lastWasNight = false;
                continue;
            }

            if (overnightX == d)
            {
                days[d] = DayState.X;
                events.Add(new DemoFixedEvent(
                    emp.Id, FixedEventType.X, null,
                    d.ToDateTime(new TimeOnly(23, 0)),
                    d.AddDays(1).ToDateTime(new TimeOnly(3, 0)),
                    "示範跨午夜 X"));
                workRun++;
                lastWasNight = true;
                continue;
            }

            if (d == rStarDay)
            {
                days[d] = DayState.RStar;
                events.Add(new DemoFixedEvent(emp.Id, FixedEventType.RStar, d, null, null, "示範歷史 R*"));
                workRun = 0;
                lastWasNight = false;
                continue;
            }

            if (lastWasNight || workRun >= 5 || d.DayOfWeek == DayOfWeek.Sunday)
            {
                days[d] = DayState.Rest;
                workRun = 0;
                lastWasNight = false;
                continue;
            }

            days[d] = DayState.Work(monthlyShift);
            workRun++;
            lastWasNight = monthlyShift == ShiftType.Night;
        }

        return days;
    }

    private static void AddTargetMonthEvents(
        IReadOnlyList<EmployeeInfo> mEmps,
        IReadOnlyList<EmployeeInfo> tEmps,
        YearMonth month,
        List<DemoFixedEvent> events)
    {
        var first = month.FirstDay;
        // R* requests in target month
        events.Add(new DemoFixedEvent(mEmps[0].Id, FixedEventType.RStar, first.AddDays(4), null, null, "示範目標月 R*"));
        events.Add(new DemoFixedEvent(mEmps[1].Id, FixedEventType.RStar, first.AddDays(11), null, null, "示範目標月 R*"));
        events.Add(new DemoFixedEvent(tEmps[0].Id, FixedEventType.RStar, first.AddDays(6), null, null, "示範目標月 R*"));
        events.Add(new DemoFixedEvent(tEmps[10].Id, FixedEventType.RStar, first.AddDays(13), null, null, "示範目標月 R*"));

        // Same-day X in target month
        var xDay = first.AddDays(7);
        events.Add(new DemoFixedEvent(
            mEmps[2].Id, FixedEventType.X, null,
            xDay.ToDateTime(new TimeOnly(8, 0)),
            xDay.ToDateTime(new TimeOnly(11, 30)),
            "示範目標月同日 X"));

        // Cross-midnight X in target month
        var ox = first.AddDays(18);
        events.Add(new DemoFixedEvent(
            tEmps[2].Id, FixedEventType.X, null,
            ox.ToDateTime(new TimeOnly(22, 30)),
            ox.AddDays(1).ToDateTime(new TimeOnly(1, 30)),
            "示範目標月跨午夜 X"));
    }
}

public sealed record DemoFixedEvent(
    string EmployeeId,
    FixedEventType Type,
    DateOnly? Date,
    DateTime? Start,
    DateTime? End,
    string? Description);

public sealed record DemoBundle(
    IReadOnlyList<EmployeeInfo> Employees,
    IReadOnlyList<CycleInfo> Cycles,
    IReadOnlyDictionary<(string EmployeeId, string Month), ShiftType> MonthlyShifts,
    IReadOnlyList<DemoFixedEvent> FixedEvents,
    IReadOnlyDictionary<(Unit Unit, string Month), IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>>> HistoryByUnitMonth,
    DateOnly HistoryFrom,
    DateOnly HistoryTo,
    YearMonth TargetMonth);
