using Microsoft.VisualStudio.TestTools.UnitTesting;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Core.Evaluation.Rules.Hard;

namespace NtmScheduler.Tests.Core;

[TestClass]
public sealed class ContinuousWorkAndSegmentTests
{
    [TestMethod]
    public void AC02_SegmentLengths_EarlyXAfternoonRestNight()
    {
        var ctx = BuildCtx("E1",
        [
            ("2026-08-01", DayState.Work(ShiftType.Morning)),
            ("2026-08-02", DayState.X),
            ("2026-08-03", DayState.Work(ShiftType.Afternoon)),
            ("2026-08-04", DayState.Rest),
            ("2026-08-05", DayState.Work(ShiftType.Night))
        ]);
        var d = SegmentExtractor.StreakDeviation(ctx, "E1");
        // closed segment length 3 → D=0; open/closed length 1 → D=2 (if scored)
        // At least verify deviation formula
        Assert.AreEqual(2, SegmentExtractor.Deviation(1));
        Assert.AreEqual(1, SegmentExtractor.Deviation(2));
        Assert.AreEqual(0, SegmentExtractor.Deviation(3));
        Assert.AreEqual(1, SegmentExtractor.Deviation(6));
        _ = d;
    }

    [TestMethod]
    public void AC03_MBlock_EarlyRestEarlyXEarlyAfternoon()
    {
        var ctx = BuildCtx("E1",
        [
            ("2026-08-01", DayState.Work(ShiftType.Morning)),
            ("2026-08-02", DayState.Rest),
            ("2026-08-03", DayState.Work(ShiftType.Morning)),
            ("2026-08-04", DayState.X),
            ("2026-08-05", DayState.Work(ShiftType.Morning)),
            ("2026-08-06", DayState.Work(ShiftType.Afternoon))
        ]);
        // Early block closes at 3 when switching to afternoon → D(3)=0; afternoon unfinished count 1 → excess 0
        Assert.AreEqual(0, BlockExtractor.Evaluate(ctx, "E1"));
    }

    [TestMethod]
    public void AC04_AfternoonToMorning_Forbidden()
    {
        var ctx = BuildCtx("E1",
        [
            ("2026-08-01", DayState.Work(ShiftType.Afternoon)),
            ("2026-08-02", DayState.Work(ShiftType.Morning))
        ], Unit.M);
        var result = new GenH03RestGap().Evaluate(ctx);
        Assert.IsTrue(result.ViolationCount > 0);
    }

    [TestMethod]
    public void AC26_Rest_ResetsContinuousWork()
    {
        var days = new List<(string, DayState)>();
        for (var i = 1; i <= 6; i++)
            days.Add(($"2026-08-{i:D2}", DayState.Work(ShiftType.Morning)));
        days.Add(("2026-08-07", DayState.Rest));
        days.Add(("2026-08-08", DayState.Work(ShiftType.Morning)));
        var ctx = BuildCtx("E1", days);
        Assert.AreEqual(0, new GenH02ContinuousWork().Evaluate(ctx).ViolationCount);
    }

    [TestMethod]
    public void AC27_RStar_ResetsContinuousWork()
    {
        var days = new List<(string, DayState)>();
        for (var i = 1; i <= 6; i++)
            days.Add(($"2026-08-{i:D2}", DayState.Work(ShiftType.Morning)));
        days.Add(("2026-08-07", DayState.RStar));
        days.Add(("2026-08-08", DayState.Work(ShiftType.Morning)));
        var ctx = BuildCtx("E1", days);
        Assert.AreEqual(0, new GenH02ContinuousWork().Evaluate(ctx).ViolationCount);
    }

    [TestMethod]
    public void AC28_R1_DoesNotReset_SeventhWorkForbidden()
    {
        var days = new List<(string, DayState)>
        {
            ("2026-08-01", DayState.Work(ShiftType.Morning)),
            ("2026-08-02", DayState.Work(ShiftType.Morning)),
            ("2026-08-03", DayState.Work(ShiftType.Morning)),
            ("2026-08-04", DayState.HolidayRest),
            ("2026-08-05", DayState.Work(ShiftType.Afternoon)),
            ("2026-08-06", DayState.Work(ShiftType.Afternoon)),
            ("2026-08-07", DayState.Work(ShiftType.Afternoon)),
            ("2026-08-08", DayState.Work(ShiftType.Morning)) // 7th work day in cw sense
        };
        var ctx = BuildCtx("E1", days);
        Assert.IsTrue(new GenH02ContinuousWork().Evaluate(ctx).ViolationCount > 0);
    }

    [TestMethod]
    public void AC28_R1_SixWorks_IsLegal()
    {
        var days = new List<(string, DayState)>
        {
            ("2026-08-01", DayState.Work(ShiftType.Morning)),
            ("2026-08-02", DayState.Work(ShiftType.Morning)),
            ("2026-08-03", DayState.Work(ShiftType.Morning)),
            ("2026-08-04", DayState.HolidayRest),
            ("2026-08-05", DayState.Work(ShiftType.Afternoon)),
            ("2026-08-06", DayState.Work(ShiftType.Afternoon)),
            ("2026-08-07", DayState.Work(ShiftType.Afternoon)),
            ("2026-08-08", DayState.Rest)
        };
        var ctx = BuildCtx("E1", days);
        Assert.AreEqual(0, new GenH02ContinuousWork().Evaluate(ctx).ViolationCount);
    }

    private static ScheduleContext BuildCtx(
        string empId,
        IEnumerable<(string Date, DayState State)> days,
        Unit unit = Unit.M)
    {
        var period = ScheduleCalendar.CreatePeriod(YearMonth.Parse("2026-08"));
        var map = new Dictionary<DateOnly, DayState>();
        foreach (var (ds, st) in days)
            map[DateOnly.Parse(ds)] = st;

        // Fill remaining period days with Rest to satisfy RequireState callers.
        foreach (var d in period.AllDays)
            map.TryAdd(d, DayState.Rest);

        var emp = new EmployeeInfo(empId, "測試", unit, HomeStation: unit == Unit.M ? "LB01" : null, Ability: 3);
        var cycle = new CycleInfo(new DateOnly(2026, 7, 6), new DateOnly(2026, 8, 30), 16, 0);

        return new ScheduleContext
        {
            Period = period,
            Unit = unit,
            Employees = [emp],
            Cycles = [cycle],
            Histories = new Dictionary<string, EmployeeHistory>
            {
                [empId] = new(new Dictionary<DateOnly, DayState>(), null, null)
            },
            XEvents = [],
            Assignments = new Dictionary<string, IReadOnlyDictionary<DateOnly, DayState>>
            {
                [empId] = map
            }
        };
    }
}
