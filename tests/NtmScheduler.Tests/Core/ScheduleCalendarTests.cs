using Microsoft.VisualStudio.TestTools.UnitTesting;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Tests.Core;

[TestClass]
public sealed class ScheduleCalendarTests
{
    [TestMethod]
    public void AC01_MonthEndingWednesday_ExtendsToSunday()
    {
        // 2026-09-30 is Wednesday → range end 2026-10-04 Sunday
        var period = ScheduleCalendar.CreatePeriod(YearMonth.Parse("2026-09"));
        Assert.AreEqual(new DateOnly(2026, 9, 1), period.FirstDay);
        Assert.AreEqual(new DateOnly(2026, 9, 30), period.MonthEnd);
        Assert.AreEqual(new DateOnly(2026, 10, 4), period.RangeEnd);
        Assert.IsTrue(period.IsExtensionDay(new DateOnly(2026, 10, 1)));
        Assert.IsFalse(period.IsExtensionDay(new DateOnly(2026, 9, 30)));
    }
}
