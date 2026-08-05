using System.Text;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Tests.Integration;

[TestClass]
public sealed class EmployeeImportTests
{
    [TestMethod]
    public async Task ImportMEmployees_RoundTrip()
    {
        await using var fx = await SqliteFixture.CreateAsync();
        const string csv = """
            employee_id,name,home_station
            M001,王小明,LB01
            M002,陳小華,LB02
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await fx.Employees.ImportCsvAsync(Unit.M, stream, "tester");
        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.AreEqual(2, result.SuccessCount);

        var list = await fx.Employees.ListAsync(Unit.M);
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("LB01", list.Single(e => e.Id == "M001").HomeStation);

        var exported = await fx.Employees.ExportCsvAsync(Unit.M);
        await using var again = new MemoryStream(exported);
        var reimport = await fx.Employees.ImportCsvAsync(Unit.M, again, "tester");
        Assert.IsTrue(reimport.Succeeded);
        Assert.AreEqual(2, reimport.SuccessCount);

        var audits = fx.Db.AuditLogs.Where(a => a.TargetType == "Employee").ToList();
        Assert.IsTrue(audits.Count >= 2);
    }

    [TestMethod]
    public async Task ImportTEmployees_ValidatesAbility()
    {
        await using var fx = await SqliteFixture.CreateAsync();
        const string bad = """
            employee_id,name,specialty,ability
            T001,陳小明,軌道,9
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(bad));
        var result = await fx.Employees.ImportCsvAsync(Unit.T, stream, "tester");
        Assert.AreEqual(0, result.SuccessCount);
        Assert.IsTrue(result.Errors.Count >= 1);

        const string good = """
            employee_id,name,specialty,ability
            T001,陳小明,軌道,4
            """;
        await using var goodStream = new MemoryStream(Encoding.UTF8.GetBytes(good));
        var ok = await fx.Employees.ImportCsvAsync(Unit.T, goodStream, "tester");
        Assert.IsTrue(ok.Succeeded);
        Assert.AreEqual(4, (await fx.Employees.ListAsync(Unit.T)).Single().Ability);
    }
}
