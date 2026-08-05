using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;

namespace NtmScheduler.Tests.Core;

[TestClass]
public sealed class RestStatsCalculatorTests
{
    [TestMethod]
    public void Counts_Month_And_Cycle_Including_History()
    {
        var month = new YearMonth(2026, 8);
        var period = ScheduleCalendar.CreatePeriod(month);
        var cycle = new CycleInfo(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 25), RequiredR: 16, RequiredR1: 2);

        var historyDays = new Dictionary<DateOnly, DayState>
        {
            [new DateOnly(2026, 7, 10)] = DayState.Rest,
            [new DateOnly(2026, 7, 11)] = DayState.RStar,
            [new DateOnly(2026, 7, 12)] = DayState.HolidayRest
        };
        var assignDays = new Dictionary<DateOnly, DayState>
        {
            [new DateOnly(2026, 8, 1)] = DayState.Rest,
            [new DateOnly(2026, 8, 2)] = DayState.HolidayRest,
            [new DateOnly(2026, 8, 3)] = DayState.Work(ShiftType.Morning)
        };

        var ctx = new ScheduleContext
        {
            Period = period,
            Unit = Unit.M,
            Employees = [new EmployeeInfo("E1", "測", Unit.M, "LB01", null, null)],
            Cycles = [cycle],
            Histories = new Dictionary<string, EmployeeHistory>
            {
                ["E1"] = new EmployeeHistory(historyDays, null, null)
            },
            XEvents = [],
            Assignments = new Dictionary<string, IReadOnlyDictionary<DateOnly, DayState>>
            {
                ["E1"] = assignDays
            }
        };

        var stats = RestStatsCalculator.Compute(ctx, "E1");
        Assert.AreEqual(1, stats.MonthGeneralRest);
        Assert.AreEqual(1, stats.MonthR1);
        Assert.AreEqual(3, stats.CycleGeneralRest); // Jul R+R* + Aug R
        Assert.AreEqual(2, stats.CycleR1);          // Jul R1 + Aug R1
        Assert.AreEqual(16, stats.RequiredR);
        Assert.AreEqual(2, stats.RequiredR1);
        Assert.AreEqual(cycle.ReservedGeneralRest(period.MonthEnd), stats.ReservedGeneralRest);
    }
}
