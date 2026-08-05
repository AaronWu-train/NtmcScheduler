using Microsoft.VisualStudio.TestTools.UnitTesting;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Core.Evaluation.Rules.Soft;

namespace NtmScheduler.Tests.Core;

[TestClass]
public sealed class TsSoftRuleTests
{
    [TestMethod]
    public void AC06_Attend_ElevenPeopleFourAttend_ShortfallOne()
    {
        var emps = Enumerable.Range(1, 11)
            .Select(i => new EmployeeInfo($"T{i:D3}", $"T{i}", Unit.T, Ability: 3, Specialty: "軌道"))
            .ToList();
        var shifts = emps.ToDictionary(e => e.Id, _ => ShiftType.Afternoon);
        var period = ScheduleCalendar.CreatePeriod(YearMonth.Parse("2026-08"));
        var assignments = new Dictionary<string, IReadOnlyDictionary<DateOnly, DayState>>();
        foreach (var e in emps)
        {
            var days = new Dictionary<DateOnly, DayState>();
            foreach (var d in period.AllDays)
                days[d] = DayState.Rest;
            assignments[e.Id] = days;
        }

        // 4 people work on 2026-08-03
        var workDay = new DateOnly(2026, 8, 3);
        for (var i = 0; i < 4; i++)
        {
            var id = emps[i].Id;
            var copy = assignments[id].ToDictionary(kv => kv.Key, kv => kv.Value);
            copy[workDay] = DayState.Work(ShiftType.Afternoon);
            assignments[id] = copy;
        }

        var ctx = new ScheduleContext
        {
            Period = period,
            Unit = Unit.T,
            Employees = emps,
            Cycles = [new CycleInfo(new DateOnly(2026, 7, 6), new DateOnly(2026, 8, 30), 16, 0)],
            Histories = emps.ToDictionary(e => e.Id, _ => new EmployeeHistory(new Dictionary<DateOnly, DayState>(), null, null)),
            XEvents = [],
            Assignments = assignments,
            MonthlyShifts = shifts
        };

        var result = new TsAttend().Evaluate(ctx);
        // floor(11/2)=5, attend 4 → shortfall 1 on that day; other days shortfall 5 each
        Assert.IsTrue(result.ViolationCount >= 1);
        Assert.IsTrue(result.Items.Any(i => i.Date == workDay && i.Message.Contains("不足 1")));
    }

    [TestMethod]
    public void AC07_Ability_DeficitOne()
    {
        var abilities = new[] { 2, 3, 3, 3 };
        var emps = abilities.Select((a, i) =>
            new EmployeeInfo($"T{i + 1}", $"T{i + 1}", Unit.T, Ability: a)).ToList();
        var shifts = emps.ToDictionary(e => e.Id, _ => ShiftType.Afternoon);
        var period = ScheduleCalendar.CreatePeriod(YearMonth.Parse("2026-08"));
        var day = new DateOnly(2026, 8, 3);
        var assignments = new Dictionary<string, IReadOnlyDictionary<DateOnly, DayState>>();
        foreach (var e in emps)
        {
            var days = period.AllDays.ToDictionary(d => d, _ => DayState.Rest);
            days[day] = DayState.Work(ShiftType.Afternoon);
            assignments[e.Id] = days;
        }

        var ctx = new ScheduleContext
        {
            Period = period,
            Unit = Unit.T,
            Employees = emps,
            Cycles = [new CycleInfo(new DateOnly(2026, 7, 6), new DateOnly(2026, 8, 30), 16, 0)],
            Histories = emps.ToDictionary(e => e.Id, _ => new EmployeeHistory(new Dictionary<DateOnly, DayState>(), null, null)),
            XEvents = [],
            Assignments = assignments,
            MonthlyShifts = shifts
        };

        var result = new TsAbility().Evaluate(ctx);
        Assert.IsTrue(result.ViolationCount >= 1);
    }
}
