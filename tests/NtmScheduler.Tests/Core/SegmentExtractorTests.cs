using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;

namespace NtmScheduler.Tests.Core;

[TestClass]
public sealed class SegmentExtractorTests
{
    [TestMethod]
    public void AC02_WorkSegments_Lengths3And1()
    {
        // 早, X, 午, R, 夜 → segments length 3 and 1
        var period = ScheduleCalendar.CreatePeriod(YearMonth.Parse("2026-08"));
        var emp = "M001";
        var days = new Dictionary<DateOnly, DayState>
        {
            [new DateOnly(2026, 8, 1)] = DayState.Work(ShiftType.Morning),
            [new DateOnly(2026, 8, 2)] = DayState.X,
            [new DateOnly(2026, 8, 3)] = DayState.Work(ShiftType.Afternoon),
            [new DateOnly(2026, 8, 4)] = DayState.Rest,
            [new DateOnly(2026, 8, 5)] = DayState.Work(ShiftType.Night),
        };
        FillRest(days, period, emp);

        var ctx = BuildCtx(period, emp, days);
        var segs = SegmentExtractor.Extract(ctx, emp, scoreOnlyTargetMonth: true)
            .Where(s => s.Closed)
            .ToList();

        CollectionAssert.AreEqual(new[] { 3, 1 }, segs.Select(s => s.Length).ToArray());
    }

    [TestMethod]
    public void Deviation_D_L_Values()
    {
        Assert.AreEqual(2, SegmentExtractor.Deviation(1));
        Assert.AreEqual(1, SegmentExtractor.Deviation(2));
        Assert.AreEqual(0, SegmentExtractor.Deviation(3));
        Assert.AreEqual(0, SegmentExtractor.Deviation(4));
        Assert.AreEqual(0, SegmentExtractor.Deviation(5));
        Assert.AreEqual(1, SegmentExtractor.Deviation(6));
        Assert.AreEqual(2, SegmentExtractor.Deviation(7));
    }

    private static void FillRest(Dictionary<DateOnly, DayState> days, SchedulePeriod period, string _)
    {
        foreach (var d in period.AllDays)
        {
            if (!days.ContainsKey(d))
                days[d] = DayState.Rest;
        }
    }

    private static ScheduleContext BuildCtx(
        SchedulePeriod period, string empId, Dictionary<DateOnly, DayState> days) =>
        new()
        {
            Period = period,
            Unit = Unit.M,
            Employees = [new EmployeeInfo(empId, "測", Unit.M, HomeStation: "LB01")],
            Cycles = [],
            Histories = new Dictionary<string, EmployeeHistory>(),
            XEvents = [],
            Assignments = new Dictionary<string, IReadOnlyDictionary<DateOnly, DayState>>
            {
                [empId] = days
            }
        };
}
