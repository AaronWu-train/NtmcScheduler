using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Core.Evaluation.Rules.Hard;

namespace NtmScheduler.Tests.Core;

[TestClass]
public sealed class GenH02Tests
{
    [TestMethod]
    public void AC26_R_ResetsContinuousWork()
    {
        var result = EvaluateSequence(
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Rest,
            DayState.Work(ShiftType.Morning));
        Assert.AreEqual(0, result.ViolationCount);
    }

    [TestMethod]
    public void AC27_RStar_ResetsContinuousWork()
    {
        var result = EvaluateSequence(
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.RStar,
            DayState.Work(ShiftType.Morning));
        Assert.AreEqual(0, result.ViolationCount);
    }

    [TestMethod]
    public void AC28_R1_DoesNotReset_NextWorkViolates()
    {
        // 早早早 R1 午午午 then another work → cw would be 7
        var states = new[]
        {
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.Work(ShiftType.Morning),
            DayState.HolidayRest,
            DayState.Work(ShiftType.Afternoon),
            DayState.Work(ShiftType.Afternoon),
            DayState.Work(ShiftType.Afternoon),
            DayState.Work(ShiftType.Morning)
        };
        var result = EvaluateSequence(states);
        Assert.IsTrue(result.ViolationCount > 0);
        Assert.IsTrue(result.Items.Any(i => i.Message.Contains("超過 6")));
    }

    private static RuleResult EvaluateSequence(params DayState[] prefix)
    {
        var period = ScheduleCalendar.CreatePeriod(YearMonth.Parse("2026-08"));
        var emp = "M001";
        var days = new Dictionary<DateOnly, DayState>();
        foreach (var d in period.AllDays)
            days[d] = DayState.Rest;

        var d0 = period.FirstDay;
        for (var i = 0; i < prefix.Length; i++)
            days[d0.AddDays(i)] = prefix[i];

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
        return new GenH02ContinuousWork().Evaluate(ctx);
    }
}
