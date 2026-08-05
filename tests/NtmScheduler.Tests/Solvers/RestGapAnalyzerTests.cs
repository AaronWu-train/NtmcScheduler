using NtmScheduler.Core.Domain;
using NtmScheduler.Solvers.Common;

namespace NtmScheduler.Tests.Solvers;

[TestClass]
public class RestGapAnalyzerTests
{
    [TestMethod]
    public void AC04_AfternoonToMorning_Forbidden()
    {
        var d = new DateOnly(2026, 8, 1);
        Assert.IsTrue(RestGapAnalyzer.ViolatesShiftPair(
            Unit.M, d, ShiftType.Afternoon, d.AddDays(1), ShiftType.Morning));

        // Gap = 8h10m < 11h
        var (_, end) = ShiftTimeConfig.Interval(Unit.M, d, ShiftType.Afternoon);
        var (start, _) = ShiftTimeConfig.Interval(Unit.M, d.AddDays(1), ShiftType.Morning);
        Assert.IsLessThan(11, (start - end).TotalHours);
        Assert.IsGreaterThan(8, (start - end).TotalHours);
    }

    [TestMethod]
    public void AC04_M_NightToMorningAndAfternoon_Forbidden()
    {
        var d = new DateOnly(2026, 8, 1);
        Assert.IsTrue(RestGapAnalyzer.ViolatesShiftPair(
            Unit.M, d, ShiftType.Night, d.AddDays(1), ShiftType.Morning));
        Assert.IsTrue(RestGapAnalyzer.ViolatesShiftPair(
            Unit.M, d, ShiftType.Night, d.AddDays(1), ShiftType.Afternoon));
    }

    [TestMethod]
    public void AC04_M_MorningToAfternoon_SameDay_And_NextMorning_Allowed()
    {
        var d = new DateOnly(2026, 8, 1);
        // Same-day 早→午 is not a sequential pair in ForbiddenNormalPairs (Order skips s1>=s2 on same day).
        // Next-day 早→早 has 16h gap — allowed.
        Assert.IsFalse(RestGapAnalyzer.ViolatesShiftPair(
            Unit.M, d, ShiftType.Morning, d.AddDays(1), ShiftType.Morning));
        Assert.IsFalse(RestGapAnalyzer.ViolatesShiftPair(
            Unit.M, d, ShiftType.Morning, d.AddDays(1), ShiftType.Afternoon));
        // Same calendar day 早 ends 14:30, 午 starts 14:20 — overlap / negative gap ⇒ forbidden by time math
        Assert.IsTrue(RestGapAnalyzer.ViolatesShiftPair(
            Unit.M, d, ShiftType.Morning, d, ShiftType.Afternoon));
    }

    [TestMethod]
    public void AC04_T_ForbiddenPairs_FromConfig()
    {
        var d = new DateOnly(2026, 8, 1);
        Assert.IsTrue(RestGapAnalyzer.ViolatesShiftPair(
            Unit.T, d, ShiftType.Afternoon, d.AddDays(1), ShiftType.Morning));
        Assert.IsTrue(RestGapAnalyzer.ViolatesShiftPair(
            Unit.T, d, ShiftType.Night, d.AddDays(1), ShiftType.Morning));
        Assert.IsTrue(RestGapAnalyzer.ViolatesShiftPair(
            Unit.T, d, ShiftType.Night, d.AddDays(1), ShiftType.Afternoon));
        // Same shift consecutive: 16h gap — allowed
        Assert.IsFalse(RestGapAnalyzer.ViolatesShiftPair(
            Unit.T, d, ShiftType.Morning, d.AddDays(1), ShiftType.Morning));
    }
}
