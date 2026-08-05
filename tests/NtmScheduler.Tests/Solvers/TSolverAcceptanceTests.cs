using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Solvers;

namespace NtmScheduler.Tests.Solvers;

[TestClass]
public class TSolverAcceptanceTests
{
    private readonly SolveService _svc = new();

    [TestMethod]
    public async Task T_Feasible_SmallFixture()
    {
        var request = FeasibleTRequest(YearMonth.Parse("2026-08"), requiredR: 10, requiredR1: 0);
        var result = await _svc.SolveAsync(request);
        Assert.AreEqual(ScheduleStatus.Feasible, result.ScheduleStatus, result.ErrorMessage);
        Assert.IsTrue(result.Candidates.Count >= 1);
    }

    [TestMethod]
    public async Task T_Infeasible_ReturnsConflictSummary()
    {
        var request = FeasibleTRequest(YearMonth.Parse("2026-08"), requiredR: 40, requiredR1: 0);
        var result = await _svc.SolveAsync(request);
        Assert.AreEqual(ScheduleStatus.Infeasible, result.ScheduleStatus);
        Assert.IsNotNull(result.TConflictSummary);
        Assert.IsTrue(result.TConflictSummary!.Message.Contains("無解"));
        Assert.IsTrue(result.TConflictSummary.CycleStats.Count >= 1);
    }

    [TestMethod]
    public async Task CrossCheck_T_AttendSpecialtyAbility()
    {
        var request = FeasibleTRequest(
            YearMonth.Parse("2026-08"),
            requiredR: 10,
            requiredR1: 0,
            softRules: SolverFixtures.Only("GEN-R-01", "T-S-ATTEND", "T-S-SPECIALTY", "T-S-ABILITY"));

        var result = await _svc.SolveAsync(request);
        Assert.AreEqual(ScheduleStatus.Feasible, result.ScheduleStatus, result.ErrorMessage);
        var cand = result.Candidates[0];
        foreach (var ruleId in new[] { "GEN-R-01", "T-S-ATTEND", "T-S-SPECIALTY", "T-S-ABILITY" })
        {
            Assert.AreEqual(
                cand.ModelMetrics[ruleId],
                cand.EvaluatorMetrics![ruleId],
                $"交叉核對 {ruleId}");
        }
    }

    private static SolveRequest FeasibleTRequest(
        YearMonth month,
        int requiredR,
        int requiredR1,
        IReadOnlyList<SoftRuleSpec>? softRules = null)
    {
        var period = ScheduleCalendar.CreatePeriod(month);
        var employees = SolverFixtures.T6();
        var cycleStart = period.FirstDay;
        var cycleEnd = period.RangeEnd;
        var cycles = new List<CycleInfo> { new(cycleStart, cycleEnd, requiredR, requiredR1) };
        var monthly = employees.ToDictionary(e => e.Id, _ => ShiftType.Morning);

        var histStart = period.FirstDay.AddDays(-7);
        var histories = new Dictionary<string, EmployeeHistory>();
        foreach (var emp in employees)
        {
            var days = new Dictionary<DateOnly, DayState>();
            var run = 0;
            for (var d = histStart; d < period.FirstDay; d = d.AddDays(1))
            {
                if (run >= 5)
                {
                    days[d] = DayState.Rest;
                    run = 0;
                }
                else
                {
                    days[d] = DayState.Work(ShiftType.Morning);
                    run++;
                }
            }

            histories[emp.Id] = new EmployeeHistory(days, null, null);
        }

        return new SolveRequest
        {
            Unit = Unit.T,
            Period = period,
            Employees = employees,
            Cycles = cycles,
            Histories = histories,
            XEvents = Array.Empty<XEvent>(),
            MonthlyShifts = monthly,
            NextMonthShifts = employees.ToDictionary(e => e.Id, _ => ShiftType.Afternoon),
            SoftRules = softRules ?? Array.Empty<SoftRuleSpec>(),
            Seed = 11,
            TotalTimeLimit = TimeSpan.FromSeconds(60),
            NumSearchWorkers = 1
        };
    }
}
