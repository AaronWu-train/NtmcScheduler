using NtmScheduler.Core.Domain;
using NtmScheduler.Infrastructure.Data.Entities;
using NtmScheduler.Infrastructure.Services;

namespace NtmScheduler.Tests.Integration;

[TestClass]
public sealed class WorkflowSafetyTests
{
    [TestMethod]
    public async Task CandidateMetrics_ReadStoredEnvelope()
    {
        await using var fx = await SqliteFixture.CreateAsync();
        var run = new ScheduleRun
        {
            Unit = Unit.M,
            TargetMonth = "2026-08",
            Status = ScheduleRunStatus.Completed
        };
        fx.Db.ScheduleRuns.Add(run);
        await fx.Db.SaveChangesAsync();
        fx.Db.CandidateSolutions.Add(new CandidateSolution
        {
            RunId = run.Id,
            Index = 1,
            MetricsJson = """{"violations":{"GEN-R-01":3},"diversityRate":0.125}"""
        });
        await fx.Db.SaveChangesAsync();

        var candidate = (await new CandidateService(fx.Db, fx.Audit).GetAsync(run.Id)).Single();

        Assert.AreEqual(3, candidate.RuleMetrics["GEN-R-01"]);
        Assert.AreEqual(0.125, candidate.DiversityRate);
    }

    [TestMethod]
    public async Task ScheduleRevalidate_MissingSchedule_Throws()
    {
        await using var fx = await SqliteFixture.CreateAsync();
        var svc = new ScheduleService(fx.Db, fx.Audit);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.RevalidateAsync(1));
    }
}
