using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.SampleData;
using NtmScheduler.Infrastructure.SampleData;
using NtmScheduler.Tests.TestData;

namespace NtmScheduler.Tests.Integration;

[TestClass]
public sealed class DemoDataSeederTests
{
    [TestMethod]
    public async Task SeedAsync_WritesExpectedEmployeeCountsAndHistory()
    {
        await using var fx = await SqliteFixture.CreateAsync();
        var seeder = new DemoDataSeeder(fx.Db, fx.Audit);

        await seeder.SeedAsync("test-op");

        var m = await fx.Db.Employees.CountAsync(e => e.Unit == Unit.M);
        var t = await fx.Db.Employees.CountAsync(e => e.Unit == Unit.T);
        var expectedM = StationConfig.AllStations.Sum(DemoDataset.StaffCountFor);
        Assert.AreEqual(expectedM, m);
        Assert.AreEqual(30, t);

        Assert.IsTrue(await fx.Db.ScheduleCycles.AnyAsync());
        Assert.IsTrue(await fx.Db.RuleSettings.AnyAsync(r => r.Unit == Unit.M));
        Assert.IsTrue(await fx.Db.RuleSettings.AnyAsync(r => r.Unit == Unit.T));
        Assert.IsGreaterThanOrEqualTo(30, await fx.Db.EmployeeMonthlyShifts.CountAsync(s => s.Month == "2026-08"));

        var rStars = await fx.Db.FixedEvents.CountAsync(e => e.Type == FixedEventType.RStar);
        var xs = await fx.Db.FixedEvents.Where(e => e.Type == FixedEventType.X).ToListAsync();
        Assert.IsGreaterThanOrEqualTo(3, rStars);
        Assert.IsGreaterThanOrEqualTo(2, xs.Count);
        Assert.IsTrue(xs.Any(e => e.Start!.Value.Date == e.End!.Value.Date), "應有同日 X");
        Assert.IsTrue(xs.Any(e => e.Start!.Value.Date < e.End!.Value.Date), "應有跨午夜 X");

        var snaps = await fx.Db.ScheduleSnapshots.CountAsync(v => v.IsCurrent);
        Assert.IsGreaterThanOrEqualTo(2, snaps);

        var assignments = await fx.Db.Assignments
            .CountAsync(a => a.OwnerType == AssignmentOwnerType.Snapshot);
        Assert.IsGreaterThan(0, assignments);

        var bundle = DemoDataset.Build();
        var m001 = await fx.Db.Assignments
            .Where(a => a.EmployeeId == "M001")
            .Select(a => a.Date)
            .ToListAsync();
        Assert.Contains(bundle.HistoryFrom, m001);
        Assert.Contains(bundle.HistoryTo, m001);

        await seeder.SeedAsync("test-op-2");
        Assert.AreEqual(expectedM, await fx.Db.Employees.CountAsync(e => e.Unit == Unit.M));
        Assert.AreEqual(30, await fx.Db.Employees.CountAsync(e => e.Unit == Unit.T));
    }

    [TestMethod]
    public void SampleDataFactory_SharesDemoDataset()
    {
        Assert.AreEqual(DemoDataset.Seed, SampleDataFactory.Seed);
        Assert.HasCount(30, SampleDataFactory.CreateTEmployees());
        Assert.AreEqual(DemoDataset.CreateMEmployees().Count, SampleDataFactory.CreateMEmployees().Count);
        Assert.AreEqual(DemoDataset.Create2026Cycles().Count, SampleDataFactory.Create2026Cycles().Count);
    }
}
