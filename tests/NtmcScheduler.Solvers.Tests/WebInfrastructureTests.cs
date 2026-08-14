using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Infrastructure.Background;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Infrastructure.Services;
using NtmcScheduler.Web.Services;

namespace NtmcScheduler.Solvers.Tests;

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
    public async Task ConfigurationCsv_ParsesIntervalsAndNonStandardShifts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new CommonConfigurationService(database.Context);
        var actor = Editor(WorkspaceCode.M);

        var intervals = await service.ParseRestIntervalsCsvAsync(
            new MemoryStream("區間開始日期,區間結束日期,國定假日日期\n2026-08-03,2026-09-27,2026-08-14\n"u8.ToArray()), actor);
        var shifts = await service.ParseNonStandardShiftsCsvAsync(
            new MemoryStream("班型,時間,代碼\n日班,08:00~17:00,DAY\n"u8.ToArray()), actor);

        Assert.AreEqual(new DateOnly(2026, 8, 3), intervals.Single().Start);
        Assert.AreEqual(new DateOnly(2026, 8, 14), intervals.Single().NationalHolidays.Single());
        Assert.AreEqual("DAY", shifts.Single().Code);
        Assert.AreEqual(new TimeOnly(17, 0), shifts.Single().EndTime);
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
        var demandService = DemandService(database.Context);
        var demand = await demandService.CreateAsync(WorkspaceCode.M, new DateOnly(2026, 10, 1), actor);
        var disposableDemand = await demandService.CreateAsync(WorkspaceCode.M, new DateOnly(2026, 9, 1), actor);

        CollectionAssert.AreEqual(
            new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1) },
            (await demandService.ListMonthsAsync(WorkspaceCode.M, actor)).ToArray());

        var previousPath = Path.GetTempFileName();
        try
        {
            ScheduleCsv.WriteMonthly(previousPath, MSolverTests.ValidInput().PreviousMonth);
            var previousBytes = await File.ReadAllBytesAsync(previousPath);
            await demandService.UploadPreviousAsync(disposableDemand.Id, new MemoryStream(previousBytes), disposableDemand.RevisionToken, actor);
            disposableDemand = (await demandService.GetAsync(WorkspaceCode.M, disposableDemand.Month, actor))!;
            Assert.IsTrue(disposableDemand.HasUploadedPreviousSchedule);
            await demandService.UploadPreviousAsync(disposableDemand.Id, new MemoryStream(previousBytes), disposableDemand.RevisionToken, actor);
            Assert.AreEqual(1, await database.Context.UploadedPreviousSchedules.CountAsync());
            disposableDemand = (await demandService.GetAsync(WorkspaceCode.M, disposableDemand.Month, actor))!;
        }
        finally { File.Delete(previousPath); }

        var queued = await new ScheduleRunService(database.Context, new ScheduleRunQueue()).QueueAsync(
            disposableDemand.Id, disposableDemand.RevisionToken, actor);
        var savedRun = await database.Context.ScheduleRuns.SingleAsync(x => x.Id == queued.Id);
        Assert.AreEqual(disposableDemand.ConfigurationRevisionId, savedRun.ConfigurationRevisionId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(savedRun.InputSnapshotJson));
        var restoredInput = System.Text.Json.JsonSerializer.Deserialize<ScheduleInput>(savedRun.InputSnapshotJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.AreEqual(intervalStart, restoredInput!.RestIntervals.Single().Start);

        await demandService.DeleteAsync(disposableDemand.Id, disposableDemand.RevisionToken, actor);
        Assert.AreEqual(1, await database.Context.ScheduleRuns.CountAsync(x => x.DemandDraftId == disposableDemand.Id));
        Assert.AreEqual(0, await database.Context.UploadedPreviousSchedules.CountAsync());
        CollectionAssert.AreEqual(new[] { new DateOnly(2026, 10, 1) }, (await demandService.ListMonthsAsync(WorkspaceCode.M, actor)).ToArray());
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "DemandDeleted"));

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
    public async Task DemandCsvImport_PreviewsThenReplacesEmployees()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        await new EmployeeService(database.Context).SaveAsync(
            new(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null), actor);
        await new CommonConfigurationService(database.Context).CreateRevisionAsync(
            [new(new(2026, 7, 20), new(2026, 9, 13), []), new(new(2026, 9, 14), new(2026, 11, 8), [])], [], null, actor);
        var service = DemandService(database.Context);
        var demand = await service.CreateAsync(WorkspaceCode.M, new(2026, 9, 1), actor);
        var input = MSolverTests.ValidInput();

        var path = Path.GetTempFileName();
        try
        {
            ScheduleCsv.WriteMonthly(path, input.DemandMonth);
            var csv = await File.ReadAllBytesAsync(path);
            var preview = await service.PreviewDemandImportAsync(demand.Id, new MemoryStream(csv), actor);
            Assert.IsTrue(preview.IsValid, string.Join(Environment.NewLine, preview.Errors));
            await service.ImportDemandAsync(demand.Id, new MemoryStream(csv), demand.RevisionToken, actor);
        }
        finally { File.Delete(path); }

        var imported = await service.GetAsync(WorkspaceCode.M, demand.Month, actor);
        Assert.HasCount(40, imported!.Employees);
        var expected = input.DemandMonth.Employees.OrderBy(x => x.EmployeeId).First();
        CollectionAssert.AreEqual(
            ScheduleCsv.MonthlyRow(input.DemandMonth, expected).ToArray(),
            imported.Employees[0].MonthlyCsvValues.ToArray());
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "DemandCsvImported"));
    }

    [TestMethod]
    public async Task DemandPrevious_AutofillsOpeningUsageAndPerpetualSchedule()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var input = MSolverTests.ValidInput();
        var previousEmployee = input.PreviousMonth.Employees[0];
        await new EmployeeService(database.Context).SaveAsync(new(null, WorkspaceCode.M, previousEmployee.EmployeeId,
            previousEmployee.Name, previousEmployee.Affiliation, null, null, null), actor);
        var configuration = await new CommonConfigurationService(database.Context).CreateRevisionAsync(
            input.RestIntervals.Select(x => new RestIntervalDto(x.Start, x.End, x.NationalHolidays.ToArray())).ToArray(), [], null, actor);
        var revision = await database.Context.ConfigurationRevisions.SingleAsync(x => x.Id == configuration.Id);
        var adoptedVersion = Version(revision, input.PreviousMonth.MonthStart, "上月採用班表");
        adoptedVersion.Employees.Add(new()
        {
            EmployeeCode = previousEmployee.EmployeeId, Name = previousEmployee.Name, Affiliation = previousEmployee.Affiliation,
            ClosingRest = 12, ClosingSpecialRest = 2, PerpetualScheduleId = "P-ADOPTED"
        });
        database.Context.AdoptedSchedules.Add(new()
        {
            Workspace = WorkspaceCode.M, Month = input.PreviousMonth.MonthStart, ScheduleVersion = adoptedVersion,
            AdoptedByUserId = actor.UserId
        });
        await database.Context.SaveChangesAsync();

        var service = DemandService(database.Context);
        var demand = await service.CreateAsync(WorkspaceCode.M, input.DemandMonth.MonthStart, actor);
        var employee = demand.Employees.Single();
        Assert.AreEqual(12, employee.OpeningRest);
        Assert.AreEqual(2, employee.OpeningSpecialRest);
        Assert.AreEqual("P-ADOPTED", employee.PerpetualScheduleId);

        var uploadEmployee = previousEmployee with { PerpetualScheduleId = "P-UPLOAD" };
        var upload = input.PreviousMonth with
        {
            Employees = input.PreviousMonth.Employees.Select(x => x.EmployeeId == uploadEmployee.EmployeeId ? uploadEmployee : x).ToArray()
        };
        var path = Path.GetTempFileName();
        try
        {
            ScheduleCsv.WriteMonthly(path, upload);
            await using var stream = File.OpenRead(path);
            await service.UploadPreviousAsync(demand.Id, stream, demand.RevisionToken, actor);
        }
        finally { File.Delete(path); }

        employee = (await service.GetAsync(WorkspaceCode.M, demand.Month, actor))!.Employees.Single();
        Assert.AreEqual(uploadEmployee.ClosingUsage!.Rest, employee.OpeningRest);
        Assert.AreEqual(uploadEmployee.ClosingUsage.SpecialRest, employee.OpeningSpecialRest);
        Assert.AreEqual("P-UPLOAD", employee.PerpetualScheduleId);
    }

    [TestMethod]
    public async Task PreviousUpload_CreatesVersionThatCanBeAdopted()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.T);
        await new EmployeeService(database.Context).SaveAsync(
            new(null, WorkspaceCode.T, "T001", "王小明", "號誌", null, 5, null), actor);
        await new CommonConfigurationService(database.Context).CreateRevisionAsync(
            [new(new(2026, 7, 20), new(2026, 9, 13), [])], [], null, actor);
        var demandService = DemandService(database.Context);
        var demand = await demandService.CreateAsync(WorkspaceCode.T, new(2026, 9, 1), actor);
        var month = new DateOnly(2026, 8, 1);
        var assignments = Enumerable.Range(0, 31).ToDictionary(
            offset => month.AddDays(offset),
            offset => offset % 7 == 6
                ? new ScheduleCell { Kind = AssignmentKind.Rest }
                : new ScheduleCell { Kind = AssignmentKind.Work, Shift = Shift.Early });
        var previous = new MonthlySchedule(month,
        [
            new EmployeeMonthlySchedule
            {
                EmployeeId = "T001", Name = "王小明", Affiliation = "號誌", Ability = 5, MonthlyShift = Shift.Early,
                OpeningUsage = new(0, 0), Assignments = assignments, ClosingUsage = new(4, 0), NormalWorkCount = 27
            }
        ]);
        var path = Path.GetTempFileName();
        try
        {
            ScheduleCsv.WriteMonthly(path, previous);
            await using var stream = File.OpenRead(path);
            await demandService.UploadPreviousAsync(demand.Id, stream, demand.RevisionToken, actor);
        }
        finally { File.Delete(path); }

        var imported = await database.Context.ScheduleVersions.SingleAsync(x => x.SourceStatus == ScheduleRunStatus.Imported);
        Assert.AreEqual(month, imported.Month);
        Assert.IsFalse(imported.HasErrors);
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "ScheduleVersionImported"));

        var scheduleService = new ScheduleService(database.Context, new ScheduleValidationService(database.Context));
        await scheduleService.AdoptAsync(imported.Id, imported.RevisionToken, actor);
        Assert.AreEqual(imported.Id, (await database.Context.AdoptedSchedules.SingleAsync()).ScheduleVersionId);
    }

    [TestMethod]
    public async Task ScheduleRunWorker_RetriesTransientSqliteWriteLock()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ntmc-worker-lock-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<NtmcDbContext>()
            .UseSqlite($"Data Source={path};Default Timeout=1;Pooling=False").Options;
        try
        {
            await using var lockingContext = new NtmcDbContext(options);
            await using var workerContext = new NtmcDbContext(options);
            await lockingContext.Database.EnsureCreatedAsync();
            await using var transaction = await lockingContext.Database.BeginTransactionAsync();
            lockingContext.AuditLogs.Add(Audit(DateTimeOffset.UtcNow, "LockHolder"));
            await lockingContext.SaveChangesAsync();
            workerContext.AuditLogs.Add(Audit(DateTimeOffset.UtcNow, "RetriedWrite"));

            var save = typeof(ScheduleRunWorker).GetMethod("SaveChangesWithSqliteRetryAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var releaseLock = Task.Run(async () =>
            {
                await Task.Delay(1500);
                await transaction.CommitAsync();
            });
            var retryingSave = (Task)save.Invoke(null, [workerContext, CancellationToken.None])!;
            await Task.WhenAll(retryingSave, releaseLock);

            Assert.AreEqual(2, await workerContext.AuditLogs.CountAsync());
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + "-shm");
            File.Delete(path + "-wal");
        }
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
    public async Task ScheduleDetail_SplitsMultipleCollectionQueries()
    {
        await using var database = await TestDatabase.CreateAsync(throwOnMultipleCollectionInclude: true);
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = Guid.NewGuid() };
        var version = Version(revision, new DateOnly(2026, 8, 1), "M 班表");
        version.Employees.Add(new ScheduleEmployeeSnapshot
        {
            EmployeeCode = "M001", Name = "王小明", Affiliation = "LB01",
            Assignments = [new ScheduleAssignment { Date = version.Month, Kind = "Rest" }]
        });
        version.ExternalAssignments.Add(new ExternalAssignment
        {
            Date = version.Month, Station = "LB09", Shift = "Early", Count = 1
        });
        database.Context.AddRange(revision, version);
        await database.Context.SaveChangesAsync();

        var service = new ScheduleService(database.Context, new ScheduleValidationService(database.Context));
        var detail = await service.GetAsync(version.Id, Editor(WorkspaceCode.M));

        Assert.AreEqual(version.Id, detail.Version.Id);
        Assert.HasCount(1, detail.Employees);
        Assert.HasCount(46, ScheduleCsv.MonthlyHeaders);
        Assert.HasCount(46, detail.Employees[0].MonthlyCsvValues);
        Assert.AreEqual("R", detail.Employees[0].MonthlyCsvValues[8]);
        Assert.HasCount(1, detail.ExternalAssignments);
    }

    [TestMethod]
    public async Task ScheduleValidation_UsesSourceRunPreviousMonthAtMonthStart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = actor.UserId };
        var august = new DateOnly(2026, 8, 1);
        var september = new DateOnly(2026, 9, 1);
        var previousAssignments = Enumerable.Range(0, 31).ToDictionary(
            offset => august.AddDays(offset),
            offset => offset == 30
                ? new ScheduleCell { Kind = AssignmentKind.Rest }
                : new ScheduleCell { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Early });
        var currentAssignments = Enumerable.Range(0, 30).ToDictionary(
            offset => september.AddDays(offset),
            offset => offset == 2
                ? new ScheduleCell { Kind = AssignmentKind.Rest }
                : new ScheduleCell { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Early });
        var previous = new MonthlySchedule(august,
        [
            new EmployeeMonthlySchedule
            {
                EmployeeId = "M001", Name = "王小明", Affiliation = "LB01",
                Assignments = previousAssignments, ClosingUsage = new(1, 0), NormalWorkCount = 30
            }
        ]);
        var current = new MonthlySchedule(september,
        [
            new EmployeeMonthlySchedule
            {
                EmployeeId = "M001", Name = "王小明", Affiliation = "LB01",
                Assignments = currentAssignments, ClosingUsage = new(1, 0), NormalWorkCount = 29
            }
        ]);
        var input = new ScheduleInput(previous, current, [], new NonStandardShiftTable([]));
        var run = new ScheduleRun
        {
            Workspace = WorkspaceCode.M, Month = september, DemandDraftId = Guid.NewGuid(),
            ConfigurationRevisionId = revision.Id, RequestedByUserId = actor.UserId, RequestedByName = actor.UserName,
            InputSnapshotJson = System.Text.Json.JsonSerializer.Serialize(input,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        };
        var version = Version(revision, september, "候選");
        version.SourceRun = run;
        version.Employees.Add(new()
        {
            EmployeeCode = "M001", Name = "王小明", Affiliation = "LB01",
            Assignments = currentAssignments.Select(pair => new ScheduleAssignment
            {
                Date = pair.Key, Kind = pair.Value.Kind!.Value.ToString(), Station = pair.Value.Station,
                Shift = pair.Value.Shift?.ToString()
            }).ToList()
        });
        database.Context.AddRange(revision, run, version);
        await database.Context.SaveChangesAsync();

        var result = await new ScheduleValidationService(database.Context).ValidateAsync(version.Id, actor);

        Assert.IsFalse(result.Issues.Any(x => x.RuleName == "連續七日至少一日一般 R" &&
            x.EmployeeCode == "M001" && x.Date is { Day: 1 or 2 }));
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
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => DemandService(database.Context).GetAsync(WorkspaceCode.M, month, anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new ScheduleRunService(database.Context, new ScheduleRunQueue()).ListAsync(WorkspaceCode.M, month, anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new ScheduleValidationService(database.Context).ValidateAsync(Guid.NewGuid(), anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new AuditQueryService(database.Context).QueryAsync(null, null, null, null, anonymous));
    }

    private static ActorContext Editor(WorkspaceCode workspace) =>
        new(Guid.NewGuid(), "editor", false, new HashSet<WorkspaceCode> { workspace }, Guid.NewGuid().ToString("N"));

    private static DemandService DemandService(NtmcDbContext db) =>
        new(db, new ScheduleValidationService(db));

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
        private readonly DbContextOptions<NtmcDbContext> options;
        public NtmcDbContext Context { get; }

        private TestDatabase(SqliteConnection connection, DbContextOptions<NtmcDbContext> options, NtmcDbContext context)
        {
            this.connection = connection;
            this.options = options;
            Context = context;
        }

        public static async Task<TestDatabase> CreateAsync(bool throwOnMultipleCollectionInclude = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<NtmcDbContext>().UseSqlite(connection);
            if (throwOnMultipleCollectionInclude)
                builder.ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning));
            var options = builder.Options;
            var context = new NtmcDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, options, context);
        }

        public NtmcDbContext NewContext() => new(options);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
