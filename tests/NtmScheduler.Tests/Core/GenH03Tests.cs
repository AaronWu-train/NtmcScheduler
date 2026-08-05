using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Core.Evaluation.Rules.Hard;

namespace NtmScheduler.Tests.Core;

[TestClass]
public sealed class GenH03Tests
{
    [TestMethod]
    public void AC04_AfternoonThenMorning_Forbidden()
    {
        var period = ScheduleCalendar.CreatePeriod(YearMonth.Parse("2026-08"));
        var emp = "M001";
        var days = new Dictionary<DateOnly, DayState>();
        foreach (var d in period.AllDays)
            days[d] = DayState.Rest;

        days[new DateOnly(2026, 8, 1)] = DayState.Work(ShiftType.Afternoon); // ends 22:20
        days[new DateOnly(2026, 8, 2)] = DayState.Work(ShiftType.Morning);   // starts 06:30

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

        var result = new GenH03RestGap().Evaluate(ctx);
        Assert.IsGreaterThan(0, result.ViolationCount);
        Assert.IsTrue(result.Items.Any(i => i.EmployeeId == emp && i.Date == new DateOnly(2026, 8, 2)));
    }
}
