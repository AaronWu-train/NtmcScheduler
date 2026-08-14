using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NtmScheduler.Contracts;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Background;
using NtmScheduler.Infrastructure.Services;
using NtmScheduler.Web.Services;

namespace NtmScheduler.Solvers.Tests;

[TestClass]
public sealed class WebInfrastructureTests
{
    [TestMethod]
    public void LoginRateLimiter_BlocksEleventhAttemptAndResetsAfterWindow()
    {
        var limiter = new LoginRateLimiter();
        var now = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            Assert.IsTrue(limiter.IsAllowed("viewer", "192.0.2.1", now));
            limiter.RecordFailure("viewer", "192.0.2.1", now);
        }

        Assert.IsFalse(limiter.IsAllowed("viewer", "198.51.100.1", now), "Account limit must apply across IP addresses.");
        Assert.IsFalse(limiter.IsAllowed("another", "192.0.2.1", now), "IP limit must apply across account names.");
        Assert.IsTrue(limiter.IsAllowed("viewer", "192.0.2.1", now.AddMinutes(15)));
    }

    [TestMethod]
    public async Task ConfigurationRevision_IsImmutableAndAudited()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new CommonConfigurationService(database.Context);
        var actor = Editor(WorkspaceCode.M);
        var start = new DateOnly(2026, 8, 3);
        var first = await service.CreateRevisionAsync(
            [new RestIntervalDto(start, start.AddDays(55), [start.AddDays(7)])],
            [new NonStandardShiftDto("日班", "DAY", new TimeOnly(8, 0), new TimeOnly(17, 0))],
            null,
            actor);
        var second = await service.CreateRevisionAsync(
            [new RestIntervalDto(start.AddDays(56), start.AddDays(111), [])],
            [new NonStandardShiftDto("公務", "EVENT", new TimeOnly(9, 0), new TimeOnly(18, 0))],
            first.CurrentRevisionToken,
            actor);

        Assert.AreEqual(1, first.Version);
        Assert.AreEqual(2, second.Version);
        var frozenFirst = await service.GetRevisionAsync(first.Id, actor);
        Assert.IsNotNull(frozenFirst);
        Assert.AreEqual(first.Id, frozenFirst.Id);
        Assert.AreEqual("DAY", frozenFirst.NonStandardShifts.Single().Code);
        Assert.AreEqual(start, frozenFirst.RestIntervals.Single().Start);
        Assert.AreEqual(second.Id, (await service.GetCurrentAsync(actor))!.Id);
        Assert.AreEqual(2, await database.Context.AuditLogs.CountAsync(x => x.Action == "ConfigurationRevisionCreated"));
    }

    [TestMethod]
    public async Task ConfigurationRevision_RejectsNonFiftySixDayInterval()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new CommonConfigurationService(database.Context);
        var start = new DateOnly(2026, 8, 3);

        await Assert.ThrowsExactlyAsync<DomainValidationException>(() => service.CreateRevisionAsync(
            [new RestIntervalDto(start, start.AddDays(54), [])], [], null, Editor(WorkspaceCode.T)));
        Assert.AreEqual(0, await database.Context.ConfigurationRevisions.CountAsync());
        Assert.AreEqual(0, await database.Context.AuditLogs.CountAsync());
    }

    [TestMethod]
    public async Task EmployeeWrite_EnforcesWorkspaceAndRevisionToken()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EmployeeService(database.Context);
        var viewer = new ActorContext(Guid.NewGuid(), "viewer", false, new HashSet<WorkspaceCode>(), "viewer-test");
        var create = new SaveEmployeeCommand(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null);

        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => service.SaveAsync(create, viewer));
        var saved = await service.SaveAsync(create, Editor(WorkspaceCode.M));
        var stale = create with { Id = saved.Id, Name = "王大明", RevisionToken = Guid.NewGuid() };
        await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(() => service.SaveAsync(stale, Editor(WorkspaceCode.M)));

        Assert.AreEqual("王小明", (await service.ListAsync(WorkspaceCode.M, Editor(WorkspaceCode.M))).Single().Name);
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "EmployeeCreated"));
    }

    [TestMethod]
    public async Task EmployeeDelete_PreservesAuditAndAllowsSameCodeToReturn()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EmployeeService(database.Context);
        var actor = Editor(WorkspaceCode.M);
        var command = new SaveEmployeeCommand(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null);
        var original = await service.SaveAsync(command, actor);
        var intervalStart = new DateOnly(2026, 8, 3);
        await new CommonConfigurationService(database.Context).CreateRevisionAsync(
            [new RestIntervalDto(intervalStart, intervalStart.AddDays(55), [])], [], null, actor);
        var demandService = new DemandService(database.Context);
        var demand = await demandService.CreateAsync(WorkspaceCode.M, new DateOnly(2026, 9, 1), actor);

        await service.DeleteAsync(original.Id, original.RevisionToken, actor);
        var returned = await service.SaveAsync(command with { Name = "王大明" }, actor);

        Assert.AreNotEqual(original.Id, returned.Id);
        Assert.AreEqual("王大明", (await service.ListAsync(WorkspaceCode.M, actor)).Single().Name);
        Assert.AreEqual("王小明", (await demandService.GetAsync(WorkspaceCode.M, demand.Month, actor))!.Employees.Single().Name);
        var audit = await database.Context.AuditLogs.SingleAsync(x => x.Action == "EmployeeDeleted");
        StringAssert.Contains(audit.BeforeJson!, "M001");
        Assert.IsNull(audit.AfterJson);
    }

    [TestMethod]
    public async Task EmployeeCsvImport_PreviewsThenCommitsAllRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EmployeeService(database.Context);
        var actor = Editor(WorkspaceCode.M);
        var csv = "ID,姓名,所屬車站,到職日期\nM001,王小明,LB01,\nM002,陳小華,LB02,2026-08-02\n"u8.ToArray();

        var preview = await service.PreviewImportAsync(WorkspaceCode.M, new MemoryStream(csv), actor);
        Assert.IsTrue(preview.IsValid, string.Join(Environment.NewLine, preview.Errors));
        Assert.IsTrue(preview.Differences.Any(x => x.StartsWith("新增：M001", StringComparison.Ordinal)));
        await service.ImportAsync(WorkspaceCode.M, new MemoryStream(csv), preview.RevisionToken, actor);

        var employees = await service.ListAsync(WorkspaceCode.M, actor);
        Assert.HasCount(2, employees);
        Assert.IsNull(employees.Single(x => x.EmployeeCode == "M001").EmploymentStartDate);
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "EmployeeCsvImported"));
    }

    [TestMethod]
    public async Task EmployeeCsvImport_InvalidRowDoesNotPartiallyWrite()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EmployeeService(database.Context);
        var csv = "ID,姓名,專業分組,到職日期,能力\nT001,王小明,號誌,2026-08-01,5\nT002,陳小華,電力,2026-08-02,9\n"u8.ToArray();

        var preview = await service.PreviewImportAsync(WorkspaceCode.T, new MemoryStream(csv), Editor(WorkspaceCode.T));

        Assert.IsFalse(preview.IsValid);
        Assert.AreEqual(0, await database.Context.Employees.CountAsync());
        Assert.AreEqual(0, await database.Context.AuditLogs.CountAsync());
    }

    [TestMethod]
    public async Task AdoptedSchedule_AllowsOnlyOneVersionPerWorkspaceMonth()
    {
        await using var database = await TestDatabase.CreateAsync();
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = Guid.NewGuid() };
        var month = new DateOnly(2026, 8, 1);
        var first = Version(revision, month, "候選 1");
        var second = Version(revision, month, "候選 2");
        database.Context.AddRange(revision, first, second);
        database.Context.AdoptedSchedules.Add(new AdoptedSchedule
        {
            Workspace = WorkspaceCode.M,
            Month = month,
            ScheduleVersion = first,
            AdoptedByUserId = Guid.NewGuid()
        });
        await database.Context.SaveChangesAsync();

        await using var conflictingContext = database.NewContext();
        conflictingContext.AdoptedSchedules.Add(new AdoptedSchedule
        {
            Workspace = WorkspaceCode.M,
            Month = month,
            ScheduleVersionId = second.Id,
            AdoptedByUserId = Guid.NewGuid()
        });
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => conflictingContext.SaveChangesAsync());
    }

    [TestMethod]
    public async Task AuditQuery_UsesTaipeiDateBoundaryOnSqlite()
    {
        await using var database = await TestDatabase.CreateAsync();
        var includedAt = new DateTimeOffset(2026, 8, 12, 16, 0, 0, TimeSpan.Zero);
        var excludedAt = includedAt.AddTicks(-1);
        database.Context.AuditLogs.AddRange(
            Audit(includedAt, "Included"),
            Audit(excludedAt, "Excluded"));
        await database.Context.SaveChangesAsync();

        var actor = new ActorContext(Guid.NewGuid(), "admin", true, new HashSet<WorkspaceCode>(), "audit-test");
        var result = await new AuditQueryService(database.Context).QueryAsync(
            new DateOnly(2026, 8, 13), new DateOnly(2026, 8, 13), null, null, actor);

        Assert.HasCount(1, result);
        Assert.AreEqual("Included", result[0].Action);
    }

    [TestMethod]
    public async Task ExternalScheduleExport_WritesBomCsvAndAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = Guid.NewGuid() };
        var version = Version(revision, new DateOnly(2026, 8, 1), "含外派班表");
        version.ExternalAssignments.Add(new ExternalAssignment
        {
            Date = new DateOnly(2026, 8, 3),
            Station = "LB09",
            Shift = "Afternoon",
            Count = 2
        });
        database.Context.AddRange(revision, version);
        await database.Context.SaveChangesAsync();

        var service = new ScheduleService(database.Context, new ScheduleValidationService(database.Context));
        var bytes = await service.ExportExternalCsvAsync(version.Id, Editor(WorkspaceCode.M));

        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        StringAssert.Contains(System.Text.Encoding.UTF8.GetString(bytes), "2026-08-03,LB09,小,2");
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "ExternalScheduleCsvDownloaded"));
    }

    [TestMethod]
    public async Task ApplicationServices_RejectForgedAnonymousActor()
    {
        await using var database = await TestDatabase.CreateAsync();
        var anonymous = new ActorContext(Guid.Empty, "anonymous", true,
            new HashSet<WorkspaceCode> { WorkspaceCode.M, WorkspaceCode.T }, "anonymous-test");
        var month = new DateOnly(2026, 8, 1);

        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new CommonConfigurationService(database.Context).GetCurrentAsync(anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new EmployeeService(database.Context).ListAsync(WorkspaceCode.M, anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new DemandService(database.Context).GetAsync(WorkspaceCode.M, month, anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new ScheduleRunService(database.Context, new ScheduleRunQueue()).ListAsync(WorkspaceCode.M, month, anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new ScheduleValidationService(database.Context).ValidateAsync(Guid.NewGuid(), anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new AuditQueryService(database.Context).QueryAsync(null, null, null, null, anonymous));
    }

    private static ActorContext Editor(WorkspaceCode workspace) =>
        new(Guid.NewGuid(), "editor", false, new HashSet<WorkspaceCode> { workspace }, Guid.NewGuid().ToString("N"));

    private static ScheduleVersion Version(ConfigurationRevision revision, DateOnly month, string name) => new()
    {
        Workspace = WorkspaceCode.M,
        Month = month,
        Name = name,
        SourceStatus = ScheduleRunStatus.Optimal,
        CreatedByUserId = Guid.NewGuid(),
        UpdatedByUserId = Guid.NewGuid(),
        ConfigurationRevision = revision
    };

    private static AuditLog Audit(DateTimeOffset at, string action) => new()
    {
        AtUtc = at,
        AtUtcTicks = at.UtcTicks,
        ActorName = "tester",
        Action = action,
        ResourceType = "Test",
        ResourceId = "test",
        CorrelationId = "test"
    };

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<NtmDbContext> options;
        public NtmDbContext Context { get; }

        private TestDatabase(SqliteConnection connection, DbContextOptions<NtmDbContext> options, NtmDbContext context)
        {
            this.connection = connection;
            this.options = options;
            Context = context;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<NtmDbContext>().UseSqlite(connection).Options;
            var context = new NtmDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, options, context);
        }

        public NtmDbContext NewContext() => new(options);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
