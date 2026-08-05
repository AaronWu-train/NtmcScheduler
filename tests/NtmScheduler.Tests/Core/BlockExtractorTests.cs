using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;

namespace NtmScheduler.Tests.Core;

[TestClass]
public sealed class BlockExtractorTests
{
    [TestMethod]
    public void AC03_MBlocks_Early3_Afternoon1()
    {
        // 早, R, 早, X, 早, 午 → 「早 3 次」與「午 1 次」
        var period = ScheduleCalendar.CreatePeriod(YearMonth.Parse("2026-08"));
        var emp = "M001";
        var days = new Dictionary<DateOnly, DayState>();
        foreach (var d in period.AllDays)
            days[d] = DayState.Rest;

        days[new DateOnly(2026, 8, 1)] = DayState.Work(ShiftType.Morning);
        days[new DateOnly(2026, 8, 2)] = DayState.Rest;
        days[new DateOnly(2026, 8, 3)] = DayState.Work(ShiftType.Morning);
        days[new DateOnly(2026, 8, 4)] = DayState.X;
        days[new DateOnly(2026, 8, 5)] = DayState.Work(ShiftType.Morning);
        days[new DateOnly(2026, 8, 6)] = DayState.Work(ShiftType.Afternoon);

        var ctx = new ScheduleContext
        {
            Period = period,
            Unit = Unit.M,
            Employees = [new EmployeeInfo(emp, "測", Unit.M, HomeStation: "LB01")],
            Cycles = [],
            Histories = new Dictionary<string, EmployeeHistory>(),
            XEvents = [],
            Assignments = new Dictionary<string, IReadOnlyDictionary<DateOnly, DayState>>
            {
                [emp] = days
            }
        };

        var blocks = BlockExtractor.Extract(ctx, emp);
        Assert.AreEqual(2, blocks.Count);
        Assert.AreEqual(ShiftType.Morning, blocks[0].Shift);
        Assert.AreEqual(3, blocks[0].Count);
        Assert.IsTrue(blocks[0].Closed);
        Assert.AreEqual(ShiftType.Afternoon, blocks[1].Shift);
        Assert.AreEqual(1, blocks[1].Count);
    }
}
