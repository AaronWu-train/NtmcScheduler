using System.Text;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Infrastructure.Csv;

namespace NtmScheduler.Tests.Integration;

[TestClass]
public sealed class ScheduleCsvRoundTripTests
{
    [TestMethod]
    public void ScheduleCsv_PreservesR1_AC34()
    {
        const string csv = """
            employee_id,name,home_station,2026-08-30,2026-08-31,2026-09-01,month_r,month_r1,cycle_r,cycle_r1
            M001,王小明,LB01,早,R*,LB02-午,1,0,12,1
            M002,陳小華,LB02,X,R1,R,1,1,11,2
            """;

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var doc = ScheduleCsv.Read(input);
        Assert.AreEqual("R1", doc.Rows.Single(r => r.EmployeeId == "M002").DayStates[new DateOnly(2026, 8, 31)]);

        var bytes = ScheduleCsv.Write(doc);
        using var again = new MemoryStream(bytes);
        var roundTrip = ScheduleCsv.Read(again);

        Assert.AreEqual("R1", roundTrip.Rows.Single(r => r.EmployeeId == "M002").DayStates[new DateOnly(2026, 8, 31)]);
        Assert.AreEqual("R*", roundTrip.Rows.Single(r => r.EmployeeId == "M001").DayStates[new DateOnly(2026, 8, 31)]);
        Assert.AreEqual("R", roundTrip.Rows.Single(r => r.EmployeeId == "M002").DayStates[new DateOnly(2026, 9, 1)]);
        Assert.AreNotEqual("R", roundTrip.Rows.Single(r => r.EmployeeId == "M002").DayStates[new DateOnly(2026, 8, 31)]);
    }

    [TestMethod]
    public async Task HistoryImport_ThenExport_PreservesR1_AC34()
    {
        await using var fx = await SqliteFixture.CreateAsync();

        const string schedule = """
            employee_id,name,home_station,2026-08-30,2026-08-31,2026-09-01,month_r,month_r1,cycle_r,cycle_r1
            M001,王小明,LB01,早,R1,R,1,1,12,1
            M002,陳小華,LB02,X,R*,午,1,0,11,0
            """;
        const string events = """
            employee_id,type,date,start,end,description
            M002,X,,2026-08-30 09:00,2026-08-30 17:00,上課
            """;

        await using var scheduleStream = new MemoryStream(Encoding.UTF8.GetBytes(schedule));
        await using var eventsStream = new MemoryStream(Encoding.UTF8.GetBytes(events));
        var result = await fx.HistoryImport.ImportAsync(scheduleStream, eventsStream, "tester");
        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.AreEqual(2, result.SuccessCount);

        var version = fx.Db.OfficialScheduleVersions.Single(v => v.IsCurrent);
        var exported = await fx.Export.ScheduleCsvAsync(
            new OwnerRef(AssignmentOwnerType.PublishedVersion, version.Id));

        await using var exportedStream = new MemoryStream(exported);
        var doc = ScheduleCsv.Read(exportedStream);
        var m001 = doc.Rows.Single(r => r.EmployeeId == "M001");
        Assert.AreEqual("R1", m001.DayStates[new DateOnly(2026, 8, 31)]);
        Assert.AreNotEqual("R", m001.DayStates[new DateOnly(2026, 8, 31)]);
    }

    [TestMethod]
    public void CsvReader_SupportsQuotedCommaAndBom()
    {
        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes("employee_id,name,home_station\nM001,\"王,小明\",LB01\n");
        using var ms = new MemoryStream(bom.Concat(body).ToArray());
        var (header, rows) = CsvReader.ReadTable(ms);
        Assert.AreEqual(3, header.Count);
        Assert.AreEqual("王,小明", rows[0][1]);
    }
}
