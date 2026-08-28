using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Infrastructure.Background;
using NtmcScheduler.Infrastructure.Csv;
using NtmcScheduler.Infrastructure.Services;
using NtmcScheduler.Web;
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
    public void IdentityRedirectManager_KeepsLocalReturnUrlsAndNeverLeavesTheApplication()
    {
        Assert.AreEqual("https://scheduler.local/schedules", Redirect("schedules"), "RedirectToLogin supplies base-relative return URLs without a leading slash.");
        Assert.AreEqual("https://scheduler.local/m/shift-times?month=2026-08", Redirect("m/shift-times?month=2026-08"), "Query strings must survive the round trip.");
        Assert.AreEqual("https://scheduler.local/Account/ChangePassword", Redirect("/Account/ChangePassword"), "Rooted paths are used by the sign-in and password flows.");
        Assert.AreEqual("https://scheduler.local/", Redirect(null));

        string[] hostile = ["//evil.example/phishing", "https://evil.example/phishing", @"/\evil.example", @"\\evil.example", "javascript:alert(1)"];
        foreach (var returnUrl in hostile)
            Assert.AreEqual("scheduler.local", new Uri(Redirect(returnUrl)).Host, $"'{returnUrl}' must not redirect off-site after a successful sign-in.");

        static string Redirect(string? returnUrl)
        {
            var navigation = new FakeNavigationManager("https://scheduler.local/");
            new IdentityRedirectManager(navigation).RedirectTo(returnUrl);
            return navigation.Target ?? throw new InvalidOperationException("RedirectTo did not navigate.");
        }
    }

    [TestMethod]
    public void UserBatchCsv_ParsesQuotedPasswordsAndWorkspacePermissions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ntmc-users-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path,
                "帳號,一次性密碼,Administrator,三鶯M,三鶯T,環狀M,環狀T\nuser01,\"temp,1234\",0,1,0,1,0\n");

            var command = UserAdministrationService.ParseBatchCsv(path).Single();

            Assert.AreEqual("user01", command.UserName);
            Assert.AreEqual("temp,1234", command.TemporaryPassword);
            Assert.IsFalse(command.IsAdministrator);
            CollectionAssert.AreEquivalent(
                new[] { WorkspaceCode.M, WorkspaceCode.YM },
                command.EditableWorkspaces.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DownloadTemplates_ContainBomAndAParsableExampleRow()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ntmc-templates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var users = CsvTemplates.Users();
            AssertTemplate(users);
            var usersPath = Write("users.csv", users);
            Assert.HasCount(1, UserAdministrationService.ParseBatchCsv(usersPath));

            foreach (var workspace in Enum.GetValues<WorkspaceCode>())
            {
                foreach (var kind in new[] { "employees", "demand", "previous", "rest-intervals", "non-standard-shifts" })
                {
                    var content = CsvTemplates.Create(workspace, kind);
                    Assert.IsNotNull(content, $"{workspace}/{kind}");
                    AssertTemplate(content);
                    var path = Write($"{workspace}-{kind}.csv", content);
                    if (kind == "demand" || kind == "previous")
                        Assert.HasCount(1, ScheduleCsv.ReadMonthly(path, new(2026, 9, 1), historical: kind == "previous", workspace: workspace).Employees);
                    if (kind == "rest-intervals") Assert.HasCount(1, ScheduleCsv.ReadRestIntervals(path));
                    if (kind == "non-standard-shifts") Assert.HasCount(1, ScheduleCsv.ReadNonStandardShifts(path).Shifts);
                }

                if (!workspace.IsStation()) continue;
                var perpetual = CsvTemplates.Create(workspace, "perpetual");
                Assert.IsNotNull(perpetual);
                AssertTemplate(perpetual);
                Assert.HasCount(1, ScheduleCsv.ReadMPerpetualSchedule(Write($"{workspace}-perpetual.csv", perpetual), workspace).Patterns);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }

        string Write(string fileName, byte[] content)
        {
            var path = Path.Combine(root, fileName);
            File.WriteAllBytes(path, content);
            return path;
        }

        static void AssertTemplate(byte[] content)
        {
            CollectionAssert.AreEqual(new byte[] { 0xef, 0xbb, 0xbf }, content[..3]);
            var lines = Encoding.UTF8.GetString(content).TrimStart('\uFEFF')
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.IsGreaterThanOrEqualTo(2, lines.Length);
            Assert.HasCount(lines[0].Split(',').Length, lines[1].Split(','));
        }
    }

    [TestMethod]
    public void UserBatchCsv_RejectsInvalidFlagsAndDuplicateUserNames()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ntmc-users-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path,
                "帳號,一次性密碼,Administrator,三鶯M,三鶯T,環狀M,環狀T\nuser01,temp1234,true,1,0,0,0\n");
            Assert.ThrowsExactly<DomainValidationException>(() => UserAdministrationService.ParseBatchCsv(path));

            File.WriteAllText(path,
                "帳號,一次性密碼,Administrator,三鶯M,三鶯T,環狀M,環狀T\nuser01,temp1234,0,1,0,0,0\nUSER01,temp5678,0,0,1,0,0\n");
            Assert.ThrowsExactly<DomainValidationException>(() => UserAdministrationService.ParseBatchCsv(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void HistoricalImport_RecalculatesStatisticsAndUsesEmployeeAbilityOrOne()
    {
        var month = new DateOnly(2026, 9, 1);
        var assignments = new Dictionary<DateOnly, ScheduleCell>
        {
            [month] = new() { Kind = AssignmentKind.Rest },
            [month.AddDays(1)] = new() { Kind = AssignmentKind.SpecialRest },
            [month.AddDays(2)] = new() { Kind = AssignmentKind.LeaveRest },
            [month.AddDays(3)] = new() { Kind = AssignmentKind.Work, Shift = Shift.Early }
        };
        var schedule = new MonthlySchedule(month,
        [
            new EmployeeMonthlySchedule
            {
                EmployeeId = "T001", Name = "王小明", Affiliation = "號誌", MonthlyShift = Shift.Early,
                Assignments = assignments
            },
            new EmployeeMonthlySchedule
            {
                EmployeeId = "T002", Name = "陳小華", Affiliation = "電力", MonthlyShift = Shift.Early,
                EmploymentStartDate = month, Assignments = assignments
            }
        ]);
        var result = SolverScheduleMapper.CompleteHistoricalImport(schedule, WorkspaceCode.T,
            new Dictionary<string, int?> { ["T001"] = 5 },
            new Dictionary<string, RestUsage> { ["T001"] = new(3, 1) },
            [new RestInterval(new DateOnly(2026, 8, 17), new DateOnly(2026, 10, 11), new HashSet<DateOnly>())]);

        Assert.AreEqual(5, result.Employees[0].Ability);
        Assert.AreEqual(1, result.Employees[1].Ability);
        Assert.AreEqual(new RestUsage(4, 2), result.Employees[0].ClosingUsage);
        Assert.AreEqual(1, result.Employees[0].NormalWorkCount);
        Assert.AreEqual(1, result.Employees[0].Assignments.Values.Count(x => x.Kind == AssignmentKind.LeaveRest));
        Assert.IsNull(result.Employees[0].RequestedLeaveRestCount);
    }

    [TestMethod]
    public void HistoricalImport_BlocksOnlyWhenOpeningUsageCannotBeDerived()
    {
        var month = new DateOnly(2026, 9, 1);
        var schedule = new MonthlySchedule(month,
        [
            new EmployeeMonthlySchedule
            {
                EmployeeId = "T001", Name = "王小明", Affiliation = "號誌", MonthlyShift = Shift.Early,
                Assignments = new Dictionary<DateOnly, ScheduleCell>()
            }
        ]);

        var error = Assert.ThrowsExactly<DomainValidationException>(() => SolverScheduleMapper.CompleteHistoricalImport(
            schedule, WorkspaceCode.T, new Dictionary<string, int?>(), new Dictionary<string, RestUsage>(),
            [new RestInterval(new DateOnly(2026, 8, 17), new DateOnly(2026, 10, 11), new HashSet<DateOnly>())]));
        StringAssert.Contains(error.Message, "找不到前月採用班表");
    }

    [TestMethod]
    public async Task UserAdministration_BatchCreateAndSoftDeleteAreAtomicAndAudited()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NtmcDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 2;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<NtmcDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using (var setup = provider.CreateAsyncScope())
            await setup.ServiceProvider.GetRequiredService<NtmcDbContext>().Database.EnsureCreatedAsync();

        var service = new UserAdministrationService(provider.GetRequiredService<IServiceScopeFactory>());
        var actor = new ActorContext(Guid.NewGuid(), "admin", true, new HashSet<WorkspaceCode>(), "user-batch-test");
        await Assert.ThrowsExactlyAsync<DomainValidationException>(() => service.CreateBatchAsync(
            Csv("帳號,一次性密碼,Administrator,三鶯M,三鶯T,環狀M,環狀T\nuser01,temp1234,0,1,0,0,0\nuser02,short,0,0,1,0,0\n"), actor));
        Assert.HasCount(0, await service.ListAsync(actor));

        await service.CreateBatchAsync(
            Csv("帳號,一次性密碼,Administrator,三鶯M,三鶯T,環狀M,環狀T\nuser01,temp1234,0,1,0,1,0\nadmin02,temp5678,1,0,0,0,0\n"), actor);
        var users = await service.ListAsync(actor);

        Assert.HasCount(2, users);
        Assert.IsTrue(users.Single(x => x.UserName == "admin02").IsAdministrator);
        CollectionAssert.AreEquivalent(
            new[] { WorkspaceCode.M, WorkspaceCode.YM },
            users.Single(x => x.UserName == "user01").EditableWorkspaces.ToArray());
        var deleted = users.Single(x => x.UserName == "user01");
        await service.DeleteAsync(deleted.Id, deleted.RevisionToken, actor);
        Assert.AreEqual("admin02", (await service.ListAsync(actor)).Single().UserName);
        await Assert.ThrowsExactlyAsync<DomainValidationException>(() =>
            service.ResetPasswordAsync(deleted.Id, "temp9999", deleted.RevisionToken, actor));
        await Assert.ThrowsExactlyAsync<DomainValidationException>(() =>
            service.UpdateAsync(deleted.Id, false, false, new HashSet<WorkspaceCode>(), deleted.RevisionToken, actor));
        var administrator = users.Single(x => x.UserName == "admin02");
        var self = actor with { UserId = administrator.Id };
        await Assert.ThrowsExactlyAsync<DomainValidationException>(() => service.DeleteAsync(administrator.Id, administrator.RevisionToken, self));

        await using var verification = provider.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var deletedEntity = await db.Users.SingleAsync(x => x.Id == deleted.Id);
        Assert.IsTrue(deletedEntity.IsDeleted);
        Assert.IsTrue(deletedEntity.IsDisabled);
        Assert.AreEqual(2, await db.AuditLogs.CountAsync(x => x.Action == "UserCreated"));
        Assert.AreEqual(1, await db.AuditLogs.CountAsync(x => x.Action == "UserDeleted"));

        static MemoryStream Csv(string text) => new(Encoding.UTF8.GetBytes(text));
    }

    [TestMethod]
    public void CurrentConfiguration_KeyIsNeverDatabaseGenerated()
    {
        var options = new DbContextOptionsBuilder<NtmcDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new NtmcDbContext(options);

        var id = db.Model.FindEntityType(typeof(CurrentConfiguration))!
            .FindProperty(nameof(CurrentConfiguration.Id))!;

        Assert.AreEqual(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, id.ValueGenerated);
    }

    [TestMethod]
    public async Task ConfigurationRevision_IsImmutableAndAudited()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new CommonConfigurationService(database);
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
        var service = new CommonConfigurationService(database);
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
        var service = new CommonConfigurationService(database);
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
        var service = new EmployeeService(database);
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
    public async Task EmployeeService_AllowsConcurrentReadsOnSeparateContexts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ntmc-concurrent-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<NtmcDbContext>().UseSqlite($"Data Source={path}").Options;
            await using (var setup = new NtmcDbContext(options))
                await setup.Database.EnsureCreatedAsync();
            var service = new EmployeeService(new OptionsDbContextFactory(options));
            var actor = Editor(WorkspaceCode.M);
            await service.SaveAsync(new(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null), actor);
            await Task.WhenAll(
                service.ListAsync(WorkspaceCode.M, actor),
                service.ListAsync(WorkspaceCode.M, actor),
                service.ListAsync(WorkspaceCode.M, actor));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task TAbility_IsOnlyReturnedToTEditors()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EmployeeService(database);
        var tEditor = Editor(WorkspaceCode.T);
        var viewer = new ActorContext(Guid.NewGuid(), "viewer", false, new HashSet<WorkspaceCode>(), "viewer-test");
        await service.SaveAsync(new(null, WorkspaceCode.T, "T001", "王小明", "號誌", null, 5, null), tEditor);

        Assert.AreEqual(5, (await service.ListAsync(WorkspaceCode.T, tEditor)).Single().Ability);
        Assert.IsNull((await service.ListAsync(WorkspaceCode.T, viewer)).Single().Ability);
        Assert.IsNull((await service.ListAsync(WorkspaceCode.T, Editor(WorkspaceCode.M))).Single().Ability);
    }

    [TestMethod]
    public async Task YtEmployeesAndAbility_AreIndependentFromT()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EmployeeService(database);
        var tEditor = Editor(WorkspaceCode.T);
        var ytEditor = Editor(WorkspaceCode.YT);
        await service.SaveAsync(new(null, WorkspaceCode.T, "T001", "三鶯員工", "號誌", null, 5, null), tEditor);
        await service.SaveAsync(new(null, WorkspaceCode.YT, "YT001", "環狀員工", "車輛", null, 4, null), ytEditor);

        Assert.AreEqual("T001", (await service.ListAsync(WorkspaceCode.T, tEditor)).Single().EmployeeCode);
        Assert.AreEqual("YT001", (await service.ListAsync(WorkspaceCode.YT, ytEditor)).Single().EmployeeCode);
        Assert.AreEqual(4, (await service.ListAsync(WorkspaceCode.YT, ytEditor)).Single().Ability);
        Assert.IsNull((await service.ListAsync(WorkspaceCode.YT, tEditor)).Single().Ability);
        Assert.IsNull((await service.ListAsync(WorkspaceCode.T, ytEditor)).Single().Ability);
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => service.SaveAsync(
            new(null, WorkspaceCode.YT, "YT002", "不可新增", "號誌", null, 3, null), tEditor));
    }

    [TestMethod]
    public async Task YtShiftTimes_DefaultToTAndRemainIndependent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new CommonConfigurationService(database);
        var tEditor = Editor(WorkspaceCode.T);
        var ytEditor = Editor(WorkspaceCode.YT);
        var start = new DateOnly(2026, 8, 3);
        var initial = await service.CreateRevisionAsync(
            [new RestIntervalDto(start, start.AddDays(55), [])], [], null, tEditor);

        Assert.AreEqual(initial.TShiftTimes, initial.YtShiftTimes);
        var custom = new WorkspaceShiftTimesDto(
            new(new TimeOnly(6, 0), new TimeOnly(14, 0)),
            new(new TimeOnly(14, 0), new TimeOnly(22, 0)),
            new(new TimeOnly(22, 0), new TimeOnly(6, 0)));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => service.UpdateWorkspaceShiftTimesAsync(
            WorkspaceCode.YT, custom, initial.CurrentRevisionToken, tEditor));
        var updated = await service.UpdateWorkspaceShiftTimesAsync(
            WorkspaceCode.YT, custom, initial.CurrentRevisionToken, ytEditor);

        Assert.AreEqual(custom, updated.YtShiftTimes);
        Assert.AreEqual(initial.TShiftTimes, updated.TShiftTimes);
        var revision = await database.Context.ConfigurationRevisions.AsNoTracking()
            .Include(x => x.StandardShiftTimes).SingleAsync(x => x.Id == updated.Id);
        var mapper = typeof(EmployeeService).Assembly.GetType("NtmcScheduler.Infrastructure.Services.SolverScheduleMapper")!;
        var solverTimes = ((NtmcScheduler.Solvers.StandardShiftTimes)mapper.GetMethod("ToStandardShiftTimes")!
            .Invoke(null, [revision, WorkspaceCode.YT])!).T;
        Assert.AreEqual(custom.Early.Start, solverTimes.Early.Start);
        Assert.AreEqual(custom.Afternoon.Start, solverTimes.Afternoon.Start);
        Assert.AreEqual(custom.Night.Start, solverTimes.Night.Start);
    }

    [TestMethod]
    public async Task ScheduleCreation_ReadsRequireMatchingWorkspaceEditor()
    {
        await using var database = await TestDatabase.CreateAsync();
        var demands = DemandService(database);
        var runs = new ScheduleRunService(database, new ScheduleRunQueue());
        var month = new DateOnly(2026, 8, 1);
        var viewer = new ActorContext(Guid.NewGuid(), "viewer", false, new HashSet<WorkspaceCode>(), "viewer-test");

        foreach (var ownWorkspace in Enum.GetValues<WorkspaceCode>())
        {
            var actor = Editor(ownWorkspace);
            var otherWorkspace = ownWorkspace == WorkspaceCode.M ? WorkspaceCode.T : WorkspaceCode.M;

            Assert.IsEmpty(await demands.ListMonthsAsync(ownWorkspace, actor));
            await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => demands.ListMonthsAsync(otherWorkspace, actor));
            await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => demands.GetAsync(otherWorkspace, month, actor));
            await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => runs.ListAsync(otherWorkspace, month, actor));
        }

        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => demands.ListMonthsAsync(WorkspaceCode.M, viewer));
    }

    [TestMethod]
    public async Task MonthlySettings_FixStationCodesCopyOperationsAndResetRestTargets()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var intervalStart = new DateOnly(2026, 8, 3);
        await new CommonConfigurationService(database).CreateRevisionAsync(
            [
                new RestIntervalDto(intervalStart, intervalStart.AddDays(55), [new(2026, 8, 14)]),
                new RestIntervalDto(intervalStart.AddDays(56), intervalStart.AddDays(111), [new(2026, 10, 12)])
            ], [], null, actor);
        await new EmployeeService(database).SaveAsync(
            new SaveEmployeeCommand(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null), actor);
        var service = DemandService(database);
        var august = await service.CreateAsync(WorkspaceCode.M, new(2026, 8, 1), actor);
        CollectionAssert.AreEqual(
            Enumerable.Range(1, 12).Select(x => $"LB{x:D2}").ToArray(),
            august.MonthlySettings.MStations.Select(x => x.Code).ToArray());

        var changedStations = august.MonthlySettings.MStations.Select((station, index) => index == 0
            ? station with { Group = "自訂群組", Early = new(0, 2), ExternalSupport = ExternalSupportPolicy.Discouraged }
            : station).ToArray();
        var staleToken = august.RevisionToken;
        august = await service.UpdateMonthlySettingsAsync(august.Id,
            august.MonthlySettings.GeneralRestTarget, august.MonthlySettings.SpecialRestTarget,
            changedStations, august.RevisionToken, actor);
        Assert.AreEqual("自訂群組", august.MonthlySettings.MStations[0].Group);
        Assert.AreEqual(new StaffingRangeDto(0, 2), august.MonthlySettings.MStations[0].Early);
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "DemandMonthlySettingsUpdated"));
        await Assert.ThrowsExactlyAsync<ConcurrencyConflictException>(() => service.UpdateMonthlySettingsAsync(
            august.Id, august.MonthlySettings.GeneralRestTarget, august.MonthlySettings.SpecialRestTarget,
            changedStations, staleToken, actor));

        var renamed = changedStations.ToArray();
        renamed[0] = renamed[0] with { Code = "OTHER" };
        await Assert.ThrowsExactlyAsync<DomainValidationException>(() => service.UpdateMonthlySettingsAsync(
            august.Id, august.MonthlySettings.GeneralRestTarget, august.MonthlySettings.SpecialRestTarget,
            renamed, august.RevisionToken, actor));

        var september = await service.CreateAsync(WorkspaceCode.M, new(2026, 9, 1), actor);
        Assert.AreEqual("自訂群組", september.MonthlySettings.MStations[0].Group);
        Assert.AreEqual(new StaffingRangeDto(0, 2), september.MonthlySettings.MStations[0].Early);
        Assert.AreEqual(8, september.MonthlySettings.GeneralRestTarget, "R target must be recalculated from the new month's calendar.");
    }

    [TestMethod]
    public void RuleWeights_RequireTheCompleteActiveSetAndAllowZero()
    {
        var weights = SolverRuleWeights.M.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        weights["ExternalStaffing"] = 0;
        Assert.AreEqual(0, SolverRuleWeights.Resolve(true, weights)["ExternalStaffing"]);

        var missing = new Dictionary<string, int>(weights, StringComparer.Ordinal);
        missing.Remove("ExternalStaffing");
        Assert.ThrowsExactly<ArgumentException>(() => SolverRuleWeights.Resolve(true, missing));
        var unknown = new Dictionary<string, int>(weights, StringComparer.Ordinal) { ["Unknown"] = 1 };
        Assert.ThrowsExactly<ArgumentException>(() => SolverRuleWeights.Resolve(true, unknown));
        var negative = new Dictionary<string, int>(weights, StringComparer.Ordinal) { ["MonthlyRest"] = -1 };
        Assert.ThrowsExactly<ArgumentException>(() => SolverRuleWeights.Resolve(true, negative));
    }

    [TestMethod]
    public void RuleDefinitions_MUseConsecutivePriorities()
    {
        var rules = new ScheduleRunService(null!, null!).GetRules(WorkspaceCode.M).Where(x => !x.IsHard).ToArray();

        CollectionAssert.AreEqual(new[] { 1, 2 }, rules.Select(x => x.Priority).Distinct().Order().ToArray());
        Assert.IsTrue(rules.Where(x => x.Key is "RequestedRest" or "UnusedLeaveRest").All(x => x.Priority == 1));
        Assert.IsTrue(rules.Where(x => x.Key is not ("RequestedRest" or "UnusedLeaveRest")).All(x => x.Priority == 2));
    }

    [TestMethod]
    public void RuleDefinitions_SoftRulesHaveExplicitFormulas()
    {
        var service = new ScheduleRunService(null!, null!);
        foreach (var workspace in Enum.GetValues<WorkspaceCode>())
            foreach (var rule in service.GetRules(workspace).Where(x => !x.IsHard))
                Assert.DoesNotContain("依目前規格", rule.Description, $"{workspace}.{rule.Key}");
    }

    [TestMethod]
    public async Task EmployeeDemandSubmission_ViewerCanFillEditorCanReimport()
    {
        await using var database = await TestDatabase.CreateAsync();
        var employees = new EmployeeService(database);
        var demands = DemandService(database);
        var submissions = SubmissionService(database);
        var editor = Editor(WorkspaceCode.M);
        var viewer = new ActorContext(Guid.NewGuid(), "viewer", false, new HashSet<WorkspaceCode>(), "viewer-test");
        var month = new DateOnly(2026, 9, 1);
        var intervalStart = new DateOnly(2026, 8, 3);
        await new CommonConfigurationService(database).CreateRevisionAsync(
            [new RestIntervalDto(intervalStart, intervalStart.AddDays(55), [])], [], null, editor);
        await employees.SaveAsync(new SaveEmployeeCommand(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null), editor);
        await employees.SaveAsync(new SaveEmployeeCommand(null, WorkspaceCode.M, "M002", "陳小華", "LB01", null, null, null), editor);
        var demand = await demands.CreateAsync(WorkspaceCode.M, month, editor);

        await submissions.UpdateLeaveRestAsync(WorkspaceCode.M, month, "M001", 2, null, viewer);
        var saved = await submissions.UpdateAssignmentAsync(WorkspaceCode.M, month, "M001", month.AddDays(4), "Work", false, "LB01", "Early", null, null, null, (await submissions.GetAsync(WorkspaceCode.M, month, "M001", viewer))!.RevisionToken, viewer);
        Assert.AreEqual(2, saved.RequestedLeaveRestCount);
        Assert.HasCount(1, saved.Assignments);

        var preview = await submissions.PreviewImportAsync(demand.Id, editor);
        Assert.IsTrue(preview.IsValid);
        Assert.AreEqual(1, preview.MatchedEmployeeCount);
        Assert.AreEqual("M001", preview.Employees.Single().EmployeeCode);
        await Assert.ThrowsExactlyAsync<DomainValidationException>(() =>
            submissions.ImportToDemandAsync(demand.Id, [], demand.RevisionToken, editor));

        demand = (await demands.GetAsync(WorkspaceCode.M, month, editor))!;
        demand = await submissions.ImportToDemandAsync(demand.Id, ["M001"], demand.RevisionToken, editor);
        var importedEmployee = demand.Employees.Single(x => x.EmployeeCode == "M001");
        Assert.AreEqual(2, importedEmployee.RequestedLeaveRestCount);
        Assert.HasCount(1, importedEmployee.Assignments);
        var firstImport = await submissions.GetImportStatusAsync(demand.Id, editor);
        Assert.IsNotNull(firstImport);
        Assert.AreEqual(editor.UserName, firstImport.ImportedByName);

        demand = await demands.UpdateAssignmentAsync(demand.Id, "M001", month.AddDays(4), "Rest", false,
            null, null, null, null, null, demand.RevisionToken, editor);

        await submissions.UpdateLeaveRestAsync(WorkspaceCode.M, month, "M001", 3, saved.RevisionToken, viewer);
        await submissions.UpdateLeaveRestAsync(WorkspaceCode.M, month, "M002", 4, null, viewer);
        preview = await submissions.PreviewImportAsync(demand.Id, editor);
        Assert.HasCount(2, preview.Employees);

        var reloaded = (await demands.GetAsync(WorkspaceCode.M, month, editor))!;
        Assert.AreEqual(2, reloaded.Employees.Single(x => x.EmployeeCode == "M001").RequestedLeaveRestCount);

        reloaded = await submissions.ImportToDemandAsync(reloaded.Id, ["M002"], reloaded.RevisionToken, editor);
        var preservedEmployee = reloaded.Employees.Single(x => x.EmployeeCode == "M001");
        Assert.AreEqual(2, preservedEmployee.RequestedLeaveRestCount);
        Assert.AreEqual("Rest", preservedEmployee.Assignments.Single(x => x.Date == month.AddDays(4)).Kind);
        Assert.AreEqual(4, reloaded.Employees.Single(x => x.EmployeeCode == "M002").RequestedLeaveRestCount);
        var latestImport = await submissions.GetImportStatusAsync(reloaded.Id, editor);
        Assert.IsNotNull(latestImport);
        Assert.IsGreaterThanOrEqualTo(firstImport.ImportedAtUtc, latestImport.ImportedAtUtc);
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => submissions.ImportToDemandAsync(reloaded.Id, ["M001"], reloaded.RevisionToken, viewer));
        Assert.AreEqual(2, await database.Context.AuditLogs.CountAsync(x => x.Action == "DemandSubmissionImported"));
        Assert.IsTrue(await database.Context.AuditLogs.AnyAsync(x => x.Action == "EmployeeDemandSubmissionUpdated"));
    }

    [TestMethod]
    public void AuditPresentation_LabelsSubmissionActions()
    {
        var labels = AuditPresentation.ActionOptions().ToDictionary(x => x.Action, x => x.Label);
        Assert.AreEqual("修改員工填報", labels["EmployeeDemandSubmissionUpdated"]);
        Assert.AreEqual("匯入員工填報", labels["DemandSubmissionImported"]);
    }

    [TestMethod]
    public async Task EmployeeDelete_PreservesAuditAndAllowsSameCodeToReturn()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EmployeeService(database);
        var actor = Editor(WorkspaceCode.M);
        var command = new SaveEmployeeCommand(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null);
        var original = await service.SaveAsync(command, actor);
        var intervalStart = new DateOnly(2026, 8, 3);
        await new CommonConfigurationService(database).CreateRevisionAsync(
            [new RestIntervalDto(intervalStart, intervalStart.AddDays(55), [])], [], null, actor);
        var demandService = DemandService(database);
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
            await demandService.UploadPreviousAsync(disposableDemand.Id, "previous.csv", new MemoryStream(previousBytes), disposableDemand.RevisionToken, actor);
            disposableDemand = (await demandService.GetAsync(WorkspaceCode.M, disposableDemand.Month, actor))!;
            Assert.IsTrue(disposableDemand.HasUploadedPreviousSchedule);
            await demandService.UploadPreviousAsync(disposableDemand.Id, "previous.csv", new MemoryStream(previousBytes), disposableDemand.RevisionToken, actor);
            Assert.AreEqual(1, await database.Context.UploadedPreviousSchedules.CountAsync());
            disposableDemand = (await demandService.GetAsync(WorkspaceCode.M, disposableDemand.Month, actor))!;
        }
        finally { File.Delete(previousPath); }

        var globalSchedule = new MPerpetualSchedule(new Dictionary<string, IReadOnlyList<ScheduleCell?>>
        {
            ["P1"] = Enumerable.Repeat<ScheduleCell?>(null, 56).ToArray()
        });
        await new MPerpetualScheduleService(database).UploadAsync(WorkspaceCode.M, "global.csv",
            new MemoryStream(ScheduleCsv.WriteMPerpetualSchedule(globalSchedule)), actor);
        var runService = new ScheduleRunService(database, new ScheduleRunQueue());
        foreach (var rejected in new[]
                 {
                     new ScheduleRunOptions(ScheduleRunOptions.MaxTimeLimitSeconds + 1, 4, 1),
                     new ScheduleRunOptions(300, ScheduleRunOptions.MaxWorkerCount + 1, 1),
                     new ScheduleRunOptions(300, 4, ScheduleRunOptions.MaxSeedCount + 1)
                 })
            await Assert.ThrowsExactlyAsync<DomainValidationException>(() => runService.QueueAsync(
                disposableDemand.Id, disposableDemand.RevisionToken, rejected, actor));
        var queued = await runService.QueueAsync(
            disposableDemand.Id, disposableDemand.RevisionToken, new ScheduleRunOptions(300, 4, 1), actor);
        var savedRun = await database.Context.ScheduleRuns.SingleAsync(x => x.Id == queued.Id);
        Assert.AreEqual(disposableDemand.ConfigurationRevisionId, savedRun.ConfigurationRevisionId);
        Assert.AreEqual(4, savedRun.WorkerCount);
        Assert.AreEqual(1, savedRun.SeedCount);
        Assert.AreEqual(300, savedRun.TimeLimitSeconds);
        Assert.IsGreaterThan(0, savedRun.RandomSeed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(savedRun.InputSnapshotJson));
        Assert.IsFalse(string.IsNullOrWhiteSpace(savedRun.PerpetualScheduleJson));
        CollectionAssert.AreEqual(new[] { queued.Id }, (await runService.ListActiveAsync(actor)).Select(x => x.Id).ToArray());
        CollectionAssert.AreEqual(new[] { queued.Id }, (await runService.ListRecentAsync(5, actor)).Select(x => x.Id).ToArray());
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
        var service = new EmployeeService(database);
        var actor = Editor(WorkspaceCode.M);
        var csv = "ID,姓名,所屬車站,到職日期,,\nM001,王小明,LB01,,,\n,,,\nM002,陳小華,LB02,2026-08-02,,\n"u8.ToArray();

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
        var service = new EmployeeService(database);
        var csv = "ID,姓名,專業分組,到職日期,能力\nT001,王小明,號誌,2026-08-01,5\nT002,陳小華,電力,2026-08-02,9\n"u8.ToArray();

        var preview = await service.PreviewImportAsync(WorkspaceCode.T, new MemoryStream(csv), Editor(WorkspaceCode.T));

        Assert.IsFalse(preview.IsValid);
        Assert.AreEqual(0, await database.Context.Employees.CountAsync());
        Assert.AreEqual(0, await database.Context.AuditLogs.CountAsync());
    }

    [TestMethod]
    public async Task EmployeeCsvImport_TAcceptsAffiliationHeader()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new EmployeeService(database);
        var csv = "ID,姓名,所屬,月中開始排班日,能力\nT001,王小明,號誌,2026-08-01,5\n"u8.ToArray();

        var preview = await service.PreviewImportAsync(WorkspaceCode.T, new MemoryStream(csv), Editor(WorkspaceCode.T));

        Assert.IsTrue(preview.IsValid, string.Join(Environment.NewLine, preview.Errors));
    }

    [TestMethod]
    public async Task DemandCsvImport_PreviewsThenReplacesEmployees()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        await new EmployeeService(database).SaveAsync(
            new(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null), actor);
        await new CommonConfigurationService(database).CreateRevisionAsync(
            [new(new(2026, 7, 20), new(2026, 9, 13), []), new(new(2026, 9, 14), new(2026, 11, 8), [])], [], null, actor);
        var service = DemandService(database);
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
        var edited = await service.UpdateAssignmentAsync(imported.Id, imported.Employees[0].EmployeeCode, imported.Month, "Rest", false,
            null, null, null, null, null, imported.RevisionToken, actor);
        Assert.AreEqual("Rest", edited.Employees[0].Assignments.Single(x => x.Date == imported.Month).Kind);
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "DemandCsvImported"));
    }

    [TestMethod]
    public async Task DemandCsvImport_BlankPerpetualScheduleInheritsFromPrevious()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var input = MSolverTests.ValidInput();
        var previousEmployee = input.DemandMonth.Employees[0];
        await new EmployeeService(database).SaveAsync(new(null, WorkspaceCode.M, previousEmployee.EmployeeId,
            previousEmployee.Name, previousEmployee.Affiliation, null, null, null), actor);
        var configuration = await new CommonConfigurationService(database).CreateRevisionAsync(
            input.RestIntervals.Select(x => new RestIntervalDto(x.Start, x.End, x.NationalHolidays.ToArray())).ToArray(), [], null, actor);
        var revision = await database.Context.ConfigurationRevisions.SingleAsync(x => x.Id == configuration.Id);
        var adoptedVersion = Version(revision, input.PreviousMonth.MonthStart, "上月採用班表");
        adoptedVersion.Employees.Add(new()
        {
            EmployeeCode = previousEmployee.EmployeeId,
            Name = previousEmployee.Name,
            Affiliation = previousEmployee.Affiliation,
            PerpetualScheduleId = "P-ADOPTED"
        });
        database.Context.AdoptedSchedules.Add(new()
        {
            Workspace = WorkspaceCode.M,
            Month = input.PreviousMonth.MonthStart,
            ScheduleVersion = adoptedVersion,
            AdoptedByUserId = actor.UserId
        });
        await database.Context.SaveChangesAsync();

        var service = DemandService(database);
        var demand = await service.CreateAsync(WorkspaceCode.M, input.DemandMonth.MonthStart, actor);
        Assert.AreEqual("P-ADOPTED", demand.Employees.Single().PerpetualScheduleId);

        var path = Path.GetTempFileName();
        try
        {
            var blank = input.DemandMonth with
            {
                Employees = [previousEmployee with { PerpetualScheduleId = null }]
            };
            ScheduleCsv.WriteMonthly(path, blank);
            await service.ImportDemandAsync(demand.Id, new MemoryStream(await File.ReadAllBytesAsync(path)), demand.RevisionToken, actor);
            demand = (await service.GetAsync(WorkspaceCode.M, demand.Month, actor))!;
            Assert.AreEqual("P-ADOPTED", demand.Employees.Single().PerpetualScheduleId);

            var overrideCsv = input.DemandMonth with
            {
                Employees = [previousEmployee with { PerpetualScheduleId = "P-CSV" }]
            };
            ScheduleCsv.WriteMonthly(path, overrideCsv);
            await service.ImportDemandAsync(demand.Id, new MemoryStream(await File.ReadAllBytesAsync(path)), demand.RevisionToken, actor);
        }
        finally { File.Delete(path); }

        demand = (await service.GetAsync(WorkspaceCode.M, demand.Month, actor))!;
        Assert.AreEqual("P-CSV", demand.Employees.Single().PerpetualScheduleId);
    }

    [TestMethod]
    public async Task DemandCrossGroupWork_IsSnapshottedAsFixedSupport()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var input = MSolverTests.ValidInput();
        var seedEmployee = input.DemandMonth.Employees.First(x => x.Affiliation == "LB01");
        await new EmployeeService(database).SaveAsync(
            new(null, WorkspaceCode.M, seedEmployee.EmployeeId, seedEmployee.Name, seedEmployee.Affiliation, null, null, null), actor);
        await new CommonConfigurationService(database).CreateRevisionAsync(
            input.RestIntervals.Select(x => new RestIntervalDto(x.Start, x.End, x.NationalHolidays.ToArray())).ToArray(), [], null, actor);
        var service = DemandService(database);
        var demand = await service.CreateAsync(WorkspaceCode.M, input.DemandMonth.MonthStart, actor);
        var path = Path.GetTempFileName();
        try
        {
            ScheduleCsv.WriteMonthly(path, input.DemandMonth);
            await service.ImportDemandAsync(demand.Id, new MemoryStream(await File.ReadAllBytesAsync(path)), demand.RevisionToken, actor);
            demand = (await service.GetAsync(WorkspaceCode.M, demand.Month, actor))!;
            var previous = input.PreviousMonth with
            {
                Employees = input.PreviousMonth.Employees.Select(x => x with
                {
                    OpeningUsage = new RestUsage(
                        x.ClosingUsage!.Rest - x.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.Rest),
                        x.ClosingUsage.SpecialRest - x.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.SpecialRest))
                }).ToArray()
            };
            ScheduleCsv.WriteMonthly(path, previous);
            await service.UploadPreviousAsync(demand.Id, "previous.csv", new MemoryStream(await File.ReadAllBytesAsync(path)), demand.RevisionToken, actor);
        }
        finally { File.Delete(path); }

        demand = (await service.GetAsync(WorkspaceCode.M, demand.Month, actor))!;
        var employee = demand.Employees.First(x => x.Affiliation == "LB01");
        demand = await service.UpdateAssignmentAsync(demand.Id, employee.EmployeeCode, demand.Month, "Work", false,
            "LB11", "Early", null, null, null, demand.RevisionToken, actor);

        var queued = await new ScheduleRunService(database, new ScheduleRunQueue()).QueueAsync(
            demand.Id, demand.RevisionToken, new ScheduleRunOptions(300, 4, 1), actor);
        var snapshot = await database.Context.ScheduleRuns.Where(x => x.Id == queued.Id).Select(x => x.InputSnapshotJson).SingleAsync();
        var solverInput = System.Text.Json.JsonSerializer.Deserialize<ScheduleInput>(snapshot, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var cell = solverInput.DemandMonth.Employees.Single(x => x.EmployeeId == employee.EmployeeCode).Assignments[demand.Month];

        Assert.AreEqual(AssignmentKind.Work, cell.Kind);
        Assert.AreEqual("LB11", cell.Station);
        Assert.AreEqual(Shift.Early, cell.Shift);
        var result = MSolver.Solve(solverInput, new SolverOptions { TimeLimit = TimeSpan.FromSeconds(3) });
        Assert.AreNotEqual(SolveStatus.InvalidInput, result.Status);
        Assert.AreEqual("LB11", result.Candidates[0].Schedule.Employees.Single(x => x.EmployeeId == employee.EmployeeCode).Assignments[demand.Month].Station);
    }

    [TestMethod]
    public async Task DemandEdits_UseFreshContextWhenExistingContextTracksStaleDraft()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        await new EmployeeService(database).SaveAsync(
            new(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null), actor);
        await new CommonConfigurationService(database).CreateRevisionAsync(
            [new(new(2026, 8, 3), new(2026, 9, 27), [])], [], null, actor);
        var service = DemandService(database);
        var demand = await service.CreateAsync(WorkspaceCode.M, new(2026, 9, 1), actor);
        var employee = demand.Employees.Single();

        var staleDraft = await database.Context.DemandDrafts.SingleAsync(x => x.Id == demand.Id);
        var staleRevision = staleDraft.RevisionToken;

        demand = await service.UpdateAssignmentAsync(demand.Id, employee.EmployeeCode, demand.Month, "Rest", false,
            null, null, null, null, null, demand.RevisionToken, actor);
        demand = await service.UpdateAssignmentAsync(demand.Id, employee.EmployeeCode, demand.Month.AddDays(1), "LeaveRest", false,
            null, null, null, null, null, demand.RevisionToken, actor);

        Assert.AreEqual(staleRevision, staleDraft.RevisionToken);
        await using var verification = database.NewContext();
        var savedEmployee = await verification.DemandEmployees.Include(x => x.Assignments).SingleAsync(x => x.Id == employee.Id);
        Assert.HasCount(2, savedEmployee.Assignments);
    }

    [TestMethod]
    public async Task DemandPrevious_AutofillsOpeningUsageAndPerpetualSchedule()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var input = MSolverTests.ValidInput();
        var previousEmployee = input.PreviousMonth.Employees[0];
        await new EmployeeService(database).SaveAsync(new(null, WorkspaceCode.M, previousEmployee.EmployeeId,
            previousEmployee.Name, previousEmployee.Affiliation, null, null, null), actor);
        var configuration = await new CommonConfigurationService(database).CreateRevisionAsync(
            input.RestIntervals.Select(x => new RestIntervalDto(x.Start, x.End, x.NationalHolidays.ToArray())).ToArray(), [], null, actor);
        var revision = await database.Context.ConfigurationRevisions.SingleAsync(x => x.Id == configuration.Id);
        var adoptedVersion = Version(revision, input.PreviousMonth.MonthStart, "上月採用班表");
        adoptedVersion.Employees.Add(new()
        {
            EmployeeCode = previousEmployee.EmployeeId,
            Name = previousEmployee.Name,
            Affiliation = previousEmployee.Affiliation,
            RequestedLeaveRestCount = 3,
            ClosingRest = 12,
            ClosingSpecialRest = 2,
            PerpetualScheduleId = "P-ADOPTED"
        });
        database.Context.AdoptedSchedules.Add(new()
        {
            Workspace = WorkspaceCode.M,
            Month = input.PreviousMonth.MonthStart,
            ScheduleVersion = adoptedVersion,
            AdoptedByUserId = actor.UserId
        });
        await database.Context.SaveChangesAsync();

        var service = DemandService(database);
        var demand = await service.CreateAsync(WorkspaceCode.M, input.DemandMonth.MonthStart, actor);
        var employee = demand.Employees.Single();
        Assert.AreEqual(12, employee.OpeningRest);
        Assert.AreEqual(2, employee.OpeningSpecialRest);
        Assert.AreEqual("P-ADOPTED", employee.PerpetualScheduleId);

        var queued = await new ScheduleRunService(database, new ScheduleRunQueue()).QueueAsync(
            demand.Id, demand.RevisionToken, new ScheduleRunOptions(300, 4, 1), actor);
        var savedRun = await database.Context.ScheduleRuns.SingleAsync(x => x.Id == queued.Id);
        using (var snapshot = System.Text.Json.JsonDocument.Parse(savedRun.InputSnapshotJson))
        {
            var requestedLeaveRestCount = snapshot.RootElement.GetProperty("previousMonth").GetProperty("employees")[0]
                .GetProperty("requestedLeaveRestCount");
            Assert.AreEqual(System.Text.Json.JsonValueKind.Null, requestedLeaveRestCount.ValueKind);
        }

        var upload = input.PreviousMonth with
        {
            Employees = input.PreviousMonth.Employees.Select(x => x with
            {
                PerpetualScheduleId = x.EmployeeId == previousEmployee.EmployeeId ? "P-UPLOAD" : x.PerpetualScheduleId,
                OpeningUsage = new RestUsage(
                    x.ClosingUsage!.Rest - x.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.Rest),
                    x.ClosingUsage.SpecialRest - x.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.SpecialRest))
            }).ToArray()
        };
        var uploadEmployee = upload.Employees.Single(x => x.EmployeeId == previousEmployee.EmployeeId);
        var path = Path.GetTempFileName();
        try
        {
            ScheduleCsv.WriteMonthly(path, upload);
            await using var stream = File.OpenRead(path);
            await service.UploadPreviousAsync(demand.Id, "previous.csv", stream, demand.RevisionToken, actor);
        }
        finally { File.Delete(path); }

        var uploadedDemand = (await service.GetAsync(WorkspaceCode.M, demand.Month, actor))!;
        employee = uploadedDemand.Employees.Single();
        Assert.AreEqual(adoptedVersion.Id, uploadedDemand.PreviousScheduleVersionId);
        Assert.AreEqual(12, employee.OpeningRest);
        Assert.AreEqual(2, employee.OpeningSpecialRest);
        Assert.AreEqual("P-ADOPTED", employee.PerpetualScheduleId);

        await service.UseUploadedPreviousScheduleAsync(uploadedDemand.Id, uploadedDemand.RevisionToken, actor);
        var reselected = (await service.GetAsync(WorkspaceCode.M, demand.Month, actor))!;
        Assert.AreEqual(PreviousScheduleSource.Upload, reselected.PreviousSource);
        Assert.AreEqual(uploadEmployee.ClosingUsage!.Rest, reselected.Employees.Single().OpeningRest);
        Assert.AreEqual(uploadEmployee.ClosingUsage.SpecialRest, reselected.Employees.Single().OpeningSpecialRest);
        Assert.AreEqual("P-UPLOAD", reselected.Employees.Single().PerpetualScheduleId);
    }

    [TestMethod]
    public async Task DemandRestorePreviousInheritedFields_RestoresOpeningUsageAndPerpetualSchedule()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var input = MSolverTests.ValidInput();
        var previousEmployee = input.PreviousMonth.Employees[0];
        await new EmployeeService(database).SaveAsync(new(null, WorkspaceCode.M, previousEmployee.EmployeeId,
            previousEmployee.Name, previousEmployee.Affiliation, null, null, null), actor);
        var configuration = await new CommonConfigurationService(database).CreateRevisionAsync(
            input.RestIntervals.Select(x => new RestIntervalDto(x.Start, x.End, x.NationalHolidays.ToArray())).ToArray(), [], null, actor);
        var revision = await database.Context.ConfigurationRevisions.SingleAsync(x => x.Id == configuration.Id);
        var adoptedVersion = Version(revision, input.PreviousMonth.MonthStart, "上月採用班表");
        adoptedVersion.Employees.Add(new()
        {
            EmployeeCode = previousEmployee.EmployeeId,
            Name = previousEmployee.Name,
            Affiliation = previousEmployee.Affiliation,
            RequestedLeaveRestCount = 3,
            ClosingRest = 12,
            ClosingSpecialRest = 2,
            PerpetualScheduleId = "P-ADOPTED"
        });
        database.Context.AdoptedSchedules.Add(new()
        {
            Workspace = WorkspaceCode.M,
            Month = input.PreviousMonth.MonthStart,
            ScheduleVersion = adoptedVersion,
            AdoptedByUserId = actor.UserId
        });
        await database.Context.SaveChangesAsync();

        var service = DemandService(database);
        var demand = await service.CreateAsync(WorkspaceCode.M, input.DemandMonth.MonthStart, actor);
        var employee = demand.Employees.Single();
        demand = await service.UpdateEmployeeAsync(demand.Id, employee.EmployeeCode, employee.EmploymentStartDate, null,
            0, 0, employee.RequestedLeaveRestCount, "CHANGED", demand.RevisionToken, actor);
        employee = demand.Employees.Single();
        Assert.AreEqual(0, employee.OpeningRest);
        Assert.AreEqual(0, employee.OpeningSpecialRest);
        Assert.AreEqual("CHANGED", employee.PerpetualScheduleId);

        demand = await service.RestorePreviousInheritedFieldsAsync(demand.Id, demand.RevisionToken, actor);
        employee = demand.Employees.Single();
        Assert.AreEqual(12, employee.OpeningRest);
        Assert.AreEqual(2, employee.OpeningSpecialRest);
        Assert.AreEqual("P-ADOPTED", employee.PerpetualScheduleId);
    }

    [TestMethod]
    public async Task TDemandMonthlyShift_UpdatePersistsAndRoundTrips()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.T);
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = actor.UserId };
        var demand = new DemandDraft
        {
            Workspace = WorkspaceCode.T,
            Month = new DateOnly(2026, 8, 1),
            ConfigurationRevision = revision,
            CreatedByUserId = actor.UserId,
            UpdatedByUserId = actor.UserId,
            Employees =
            [
                new DemandEmployee
                {
                    EmployeeCode = "T001",
                    Name = "王小明",
                    Affiliation = "號誌",
                    Ability = 5
                }
            ]
        };
        database.Context.Add(demand);
        await database.Context.SaveChangesAsync();

        var result = await DemandService(database).UpdateEmployeeAsync(demand.Id, "T001", null, "Early",
            null, null, 0, null, demand.RevisionToken, actor);

        Assert.AreEqual("Early", result.Employees.Single().MonthlyShift);
        await using var persisted = database.NewContext();
        Assert.AreEqual("Early", await persisted.DemandEmployees.AsNoTracking()
            .Where(x => x.DemandDraftId == demand.Id).Select(x => x.MonthlyShift).SingleAsync());
    }

    [TestMethod]
    public async Task PerpetualUpload_StoresMetadataAndCanBeDownloaded()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        await new EmployeeService(database).SaveAsync(
            new(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null), actor);
        await new CommonConfigurationService(database).CreateRevisionAsync(
            [new(new(2026, 8, 3), new(2026, 9, 27), [])], [], null, actor);
        var service = DemandService(database);
        var demand = await service.CreateAsync(WorkspaceCode.M, new(2026, 9, 1), actor);
        var schedule = new MPerpetualSchedule(new Dictionary<string, IReadOnlyList<ScheduleCell?>>
        {
            ["P1"] = [new ScheduleCell { Kind = AssignmentKind.Rest }, .. Enumerable.Repeat<ScheduleCell?>(null, 55)]
        });

        await service.UploadPerpetualScheduleAsync(demand.Id, "my-pattern.csv",
            new MemoryStream(ScheduleCsv.WriteMPerpetualSchedule(schedule)), demand.RevisionToken, actor);

        var saved = await service.GetAsync(WorkspaceCode.M, demand.Month, actor);
        Assert.AreEqual("my-pattern.csv", saved!.PerpetualUpload!.FileName);
        var file = await service.ExportPerpetualScheduleAsync(demand.Id, actor);
        Assert.AreEqual("my-pattern.csv", file.FileName);
        StringAssert.Contains(System.Text.Encoding.UTF8.GetString(file.Content), "P1,R");
        await service.ClearPerpetualScheduleAsync(demand.Id, saved.RevisionToken, actor);
        Assert.IsNull((await service.GetAsync(WorkspaceCode.M, demand.Month, actor))!.PerpetualUpload);

        var cleared = await service.GetAsync(WorkspaceCode.M, demand.Month, actor);
        await service.UseEmptyPerpetualScheduleAsync(demand.Id, cleared!.RevisionToken, actor);
        var empty = await service.GetAsync(WorkspaceCode.M, demand.Month, actor);
        Assert.IsTrue(empty!.PerpetualUpload!.IsEmpty);
    }

    [TestMethod]
    public async Task GlobalPerpetualSchedule_CanBeUploadedEditedAndDeleted()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var service = new MPerpetualScheduleService(database);
        ScheduleCell?[] cells =
        [
            new() { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Early },
            new() { Kind = AssignmentKind.Work, Station = "LB02", Shift = Shift.Afternoon },
            new() { Kind = AssignmentKind.Work, Station = "LB03", Shift = Shift.Night },
            new() { Kind = AssignmentKind.Rest },
            .. Enumerable.Repeat<ScheduleCell?>(null, 52)
        ];
        var uploaded = await service.UploadAsync(WorkspaceCode.M, "global.csv", new MemoryStream(ScheduleCsv.WriteMPerpetualSchedule(
            new MPerpetualSchedule(new Dictionary<string, IReadOnlyList<ScheduleCell?>> { ["P1"] = cells }))), actor);

        Assert.AreEqual("global.csv", uploaded.FileName);
        Assert.AreEqual(1, uploaded.Patterns.Single().EarlyCount);
        Assert.AreEqual(1, uploaded.Patterns.Single().AfternoonCount);
        Assert.AreEqual(1, uploaded.Patterns.Single().NightCount);
        var days = uploaded.Patterns.Single().Days.ToArray();
        days[0] = "LB12夜";
        var renamed = await service.SavePatternAsync(WorkspaceCode.M, "P1", "P-NEW", days, uploaded.RevisionToken, actor);
        Assert.AreEqual(0, renamed.Patterns.Single().EarlyCount);
        Assert.AreEqual(2, renamed.Patterns.Single().NightCount);
        var added = await service.SavePatternAsync(WorkspaceCode.M, null, "P2", Enumerable.Repeat("", 56).ToArray(), renamed.RevisionToken, actor);
        var deleted = await service.DeletePatternAsync(WorkspaceCode.M, "P2", added.RevisionToken, actor);
        CollectionAssert.AreEqual(new[] { "P-NEW" }, deleted!.Patterns.Select(x => x.Id).ToArray());
        Assert.AreEqual(4, await database.Context.AuditLogs.CountAsync());
    }

    [TestMethod]
    public async Task YmDefaultsAndPerpetualSchedule_AreIndependentFromM()
    {
        await using var database = await TestDatabase.CreateAsync();
        var mActor = Editor(WorkspaceCode.M);
        var ymActor = Editor(WorkspaceCode.YM);
        var viewer = new ActorContext(Guid.NewGuid(), "viewer", false, new HashSet<WorkspaceCode>(), "viewer-test");
        var perpetualService = new MPerpetualScheduleService(database);
        var mSchedule = new MPerpetualSchedule(new Dictionary<string, IReadOnlyList<ScheduleCell?>>
        {
            ["M-P1"] = [new ScheduleCell { Kind = AssignmentKind.Work, Station = "LB01", Shift = Shift.Early }, .. Enumerable.Repeat<ScheduleCell?>(null, 55)]
        });
        var ymSchedule = new MPerpetualSchedule(new Dictionary<string, IReadOnlyList<ScheduleCell?>>
        {
            ["Y-P1"] = [new ScheduleCell { Kind = AssignmentKind.Work, Station = "Y06", Shift = Shift.Early }, .. Enumerable.Repeat<ScheduleCell?>(null, 55)]
        });

        await perpetualService.UploadAsync(WorkspaceCode.M, "m.csv", new MemoryStream(ScheduleCsv.WriteMPerpetualSchedule(mSchedule)), mActor);
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => perpetualService.UploadAsync(
            WorkspaceCode.YM, "ym.csv", new MemoryStream(ScheduleCsv.WriteMPerpetualSchedule(ymSchedule, WorkspaceCode.YM)), mActor));
        await perpetualService.UploadAsync(WorkspaceCode.YM, "ym.csv", new MemoryStream(ScheduleCsv.WriteMPerpetualSchedule(ymSchedule, WorkspaceCode.YM)), ymActor);

        Assert.AreEqual("M-P1", (await perpetualService.GetAsync(WorkspaceCode.M, viewer))!.Patterns.Single().Id);
        Assert.AreEqual("Y-P1", (await perpetualService.GetAsync(WorkspaceCode.YM, viewer))!.Patterns.Single().Id);

        await new CommonConfigurationService(database).CreateRevisionAsync(
            [new(new(2026, 8, 3), new(2026, 9, 27), [])], [], null, ymActor);
        await new EmployeeService(database).SaveAsync(
            new(null, WorkspaceCode.YM, "YM001", "王小明", "Y06", null, null, null), ymActor);
        var demand = await DemandService(database).CreateAsync(WorkspaceCode.YM, new(2026, 9, 1), ymActor);

        CollectionAssert.AreEqual(WorkspaceCodes.YmStations.ToArray(), demand.MonthlySettings.MStations.Select(x => x.Code).ToArray());
        CollectionAssert.AreEqual(
            new[] { "G1", "G1", "G1", "G2", "G2", "G2", "G3", "G3", "G3", "G4", "G5", "G5", "G6", "G6" },
            demand.MonthlySettings.MStations.Select(x => x.Group).ToArray());
        Assert.IsTrue(demand.MonthlySettings.MStations.All(x =>
            x.ExternalSupport == ExternalSupportPolicy.Disallowed && x.Early == new StaffingRangeDto(1, 1)
            && x.Afternoon == new StaffingRangeDto(1, 1) && x.Night == new StaffingRangeDto(1, 1)));
    }

    [TestMethod]
    public async Task AddYmWorkspaceMigration_PreservesExistingMPerpetualSchedule()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NtmcDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly("NtmcScheduler.Migrations.Sqlite"))
            .Options;
        await using var db = new NtmcDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260821041259_MakeCurrentConfigurationKeyExplicit");
        var userId = Guid.NewGuid();
        var revisionToken = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO MPerpetualScheduleTemplates
                (Id, FileName, ScheduleJson, UpdatedByUserId, UpdatedAtUtc, RevisionToken)
            VALUES
                (1, {"existing-m.csv"}, {"{\"patterns\":{}}"}, {userId}, {DateTimeOffset.UtcNow}, {revisionToken})
            """);

        await migrator.MigrateAsync();

        var existing = await db.MPerpetualScheduleTemplates.AsNoTracking().SingleAsync();
        Assert.AreEqual(1, existing.Id);
        Assert.AreEqual("existing-m.csv", existing.FileName);
        Assert.AreEqual(WorkspaceCode.M, existing.Workspace);
        Assert.AreEqual(revisionToken, existing.RevisionToken);
    }

    [TestMethod]
    public async Task ScheduleListImport_CreatesIndependentMHistoryVersion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var input = MSolverTests.ValidInput();
        await new CommonConfigurationService(database).CreateRevisionAsync(
            input.RestIntervals.Select(x => new RestIntervalDto(x.Start, x.End, x.NationalHolidays.ToArray())).ToArray(), [], null, actor);
        var path = Path.GetTempFileName();
        try
        {
            var previous = input.PreviousMonth with
            {
                Employees = input.PreviousMonth.Employees.Select(x => x with
                {
                    OpeningUsage = new RestUsage(
                        x.ClosingUsage!.Rest - x.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.Rest),
                        x.ClosingUsage.SpecialRest - x.Assignments.Values.Count(cell => cell.Kind == AssignmentKind.SpecialRest))
                }).ToArray()
            };
            ScheduleCsv.WriteMonthly(path, previous);
            await using var csv = File.OpenRead(path);
            var imported = await new ScheduleService(database, new ScheduleValidationService(database))
                .ImportAsync(WorkspaceCode.M, input.PreviousMonth.MonthStart, "history.csv", csv, actor);
            Assert.AreEqual(input.PreviousMonth.MonthStart, imported.Month);
            Assert.AreEqual(ScheduleRunStatus.Imported, imported.SourceStatus);
            Assert.IsFalse(imported.IsAdopted);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task DemandAndScheduleCsv_RoundTripWorkEventAnnotation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var input = MSolverTests.ValidInput();
        var employeeService = new EmployeeService(database);
        foreach (var employee in input.DemandMonth.Employees)
            await employeeService.SaveAsync(new(null, WorkspaceCode.M, employee.EmployeeId, employee.Name, employee.Affiliation, employee.EmploymentStartDate, null, null), actor);
        await new CommonConfigurationService(database).CreateRevisionAsync(
            input.RestIntervals.Select(x => new RestIntervalDto(x.Start, x.End, x.NationalHolidays.ToArray())).ToArray(),
            [new NonStandardShiftDto("日一", "0837", new TimeOnly(8, 30), new TimeOnly(17, 30))],
            null,
            actor);

        var demandService = DemandService(database);
        var demand = await demandService.CreateAsync(WorkspaceCode.M, input.DemandMonth.MonthStart, actor);
        var schedulePath = Path.GetTempFileName();
        try
        {
            ScheduleCsv.WriteMonthly(schedulePath, input.DemandMonth);
            var lines = await File.ReadAllLinesAsync(schedulePath);
            var fields = lines[1].Split(',');
            fields[8] = "X[08:30-17:30|日一]";
            lines[1] = string.Join(',', fields);
            await File.WriteAllLinesAsync(schedulePath, lines);

            await demandService.ImportDemandAsync(demand.Id, new MemoryStream(await File.ReadAllBytesAsync(schedulePath)), demand.RevisionToken, actor);
            var importedDemand = (await demandService.GetAsync(WorkspaceCode.M, demand.Month, actor))!;
            var importedEmployee = importedDemand.Employees.Single(x => x.EmployeeCode == input.DemandMonth.Employees[0].EmployeeId);
            var assignment = importedEmployee.Assignments.Single(x => x.Date == demand.Month);
            Assert.AreEqual("日一", assignment.EventDescription);
            Assert.AreEqual("X[08:30-17:30|日一]", importedEmployee.MonthlyCsvValues[8]);

            var scheduleService = new ScheduleService(database, new ScheduleValidationService(database));
            await using var stream = File.OpenRead(schedulePath);
            var importedVersion = await scheduleService.ImportAsync(WorkspaceCode.M, demand.Month, "annotated.csv", stream, actor);
            var importedDetail = await scheduleService.GetAsync(importedVersion.Id, actor);
            Assert.AreEqual("日一", importedDetail.Assignments.Single(x =>
                x.EmployeeCode == input.DemandMonth.Employees[0].EmployeeId && x.Date == demand.Month).EventDescription);

            var exported = System.Text.Encoding.UTF8.GetString(await scheduleService.ExportCsvAsync(importedVersion.Id, actor));
            StringAssert.Contains(exported, "X[08:30-17:30|日一]");
        }
        finally
        {
            File.Delete(schedulePath);
        }
    }

    [TestMethod]
    public async Task ScheduleValidation_TAttendanceUsesHalfOfMonthlyShiftGroup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.T);
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = actor.UserId };
        var month = new DateOnly(2026, 9, 1);
        var monthDates = Enumerable.Range(0, 30).Select(month.AddDays).ToArray();
        var version = Version(revision, month, "候選");
        version.Workspace = WorkspaceCode.T;
        for (var index = 1; index <= 10; index++)
        {
            version.Employees.Add(new()
            {
                EmployeeCode = $"T{index:000}",
                Name = $"檢修{index:000}",
                Affiliation = "號誌",
                Ability = 4,
                MonthlyShift = "Night",
                Assignments = monthDates.Select(date => new ScheduleAssignment
                {
                    Date = date,
                    Kind = index <= 6 ? "Work" : "Rest",
                    Shift = index <= 6 ? "Night" : null
                }).ToList()
            });
        }
        database.Context.AddRange(revision, version);
        await database.Context.SaveChangesAsync();

        var result = await new ScheduleValidationService(database).ValidateAsync(version.Id, actor);

        Assert.IsFalse(result.Issues.Any(x => x.RuleName == "班組出勤不足"));
    }

    [TestMethod]
    public async Task PreviousUpload_StoresDemandHistoryWithoutCreatingScheduleVersion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.T);
        await new EmployeeService(database).SaveAsync(
            new(null, WorkspaceCode.T, "T001", "王小明", "號誌", null, 5, null), actor);
        await new CommonConfigurationService(database).CreateRevisionAsync(
            [new(new(2026, 7, 20), new(2026, 9, 13), [])], [], null, actor);
        var demandService = DemandService(database);
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
            File.WriteAllBytes(path, ScheduleCsv.WriteMonthlyTemplate(previous, WorkspaceCode.T, historical: true));
            await using var stream = File.OpenRead(path);
            await demandService.UploadPreviousAsync(demand.Id, "previous.csv", stream, demand.RevisionToken, actor);
        }
        finally { File.Delete(path); }

        Assert.AreEqual(1, await database.Context.UploadedPreviousSchedules.CountAsync());
        Assert.AreEqual(0, await database.Context.ScheduleVersions.CountAsync());
        Assert.AreEqual(0, await database.Context.AuditLogs.CountAsync(x => x.Action == "ScheduleVersionImported"));
        var preview = await demandService.GetPreviousSchedulePreviewAsync(demand.Id, actor);
        Assert.AreEqual(month, preview.Month);
        Assert.AreEqual("T001", preview.Employees.Single().EmployeeCode);
        Assert.AreEqual(4, preview.Employees.Single().ClosingRest);
        Assert.AreEqual(0, preview.Employees.Single().ClosingSpecialRest);
        Assert.AreEqual("5", preview.Employees.Single().MonthlyCsvValues[4]);
        Assert.AreEqual("4", preview.Employees.Single().MonthlyCsvValues[39]);
        Assert.AreEqual("0", preview.Employees.Single().MonthlyCsvValues[41]);
        Assert.AreEqual("4", preview.Employees.Single().MonthlyCsvValues[42]);
        Assert.AreEqual("27", preview.Employees.Single().MonthlyCsvValues[44]);
        var file = await demandService.ExportPreviousScheduleAsync(demand.Id, actor);
        Assert.AreEqual("previous.csv", file.FileName);
        Assert.AreEqual(0xEF, file.Content[0]);
        Assert.IsTrue(System.Text.Encoding.UTF8.GetString(file.Content)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Split(',').Contains("能力"));
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(file.Content)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Split(',').Contains("萬年班表"));
    }

    [TestMethod]
    public async Task ScheduleRunCancel_RequiresWorkspaceEditorAndRejectsFinishedRuns()
    {
        await using var database = await TestDatabase.CreateAsync();
        var queue = new ScheduleRunQueue();
        var service = new ScheduleRunService(database, queue);
        var run = QueuedRun(WorkspaceCode.M);
        database.Context.ScheduleRuns.Add(run);
        var finished = QueuedRun(WorkspaceCode.M);
        finished.Status = ScheduleRunStatus.Optimal;
        database.Context.ScheduleRuns.Add(finished);
        await database.Context.SaveChangesAsync();
        await queue.QueueAsync(run.Id);
        await queue.QueueAsync(finished.Id);

        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => service.CancelAsync(run.Id, Editor(WorkspaceCode.T)));
        Assert.IsFalse(queue.CancellationFor(run.Id).IsCancellationRequested, "A rejected request must not signal the run.");
        await Assert.ThrowsExactlyAsync<DomainValidationException>(() => service.CancelAsync(finished.Id, Editor(WorkspaceCode.M)));

        await service.CancelAsync(run.Id, Editor(WorkspaceCode.M));
        Assert.IsTrue(queue.CancellationFor(run.Id).IsCancellationRequested);
        Assert.IsTrue(await database.Context.AuditLogs.AnyAsync(x => x.Action == "ScheduleRunCancelled"));
        await Assert.ThrowsExactlyAsync<DomainValidationException>(() => service.CancelAsync(run.Id, Editor(WorkspaceCode.M)));

        queue.Release(run.Id);
        Assert.IsFalse(queue.Cancel(run.Id), "A released run is no longer cancellable.");
    }

    [TestMethod]
    public async Task ScheduleRunWorker_CancelledWhileQueued_EndsCancelledWithoutSolving()
    {
        await using var database = await TestDatabase.CreateAsync();
        var queue = new ScheduleRunQueue();
        // An unparseable snapshot fails the run loudly if the worker ever reaches the solver.
        var run = QueuedRun(WorkspaceCode.M);
        run.InputSnapshotJson = "not a schedule input";
        database.Context.ScheduleRuns.Add(run);
        await database.Context.SaveChangesAsync();
        await queue.QueueAsync(run.Id);
        await new ScheduleRunService(database, queue).CancelAsync(run.Id, Editor(WorkspaceCode.M));

        var services = new ServiceCollection();
        services.AddScoped(_ => database.NewContext());
        await using var provider = services.BuildServiceProvider();
        var worker = new ScheduleRunWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ScheduleRunWorker>.Instance);
        var process = typeof(ScheduleRunWorker).GetMethod("ProcessAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)process.Invoke(worker, [run.Id, CancellationToken.None])!;

        await using var verification = database.NewContext();
        var stored = await verification.ScheduleRuns.SingleAsync(x => x.Id == run.Id);
        Assert.AreEqual(ScheduleRunStatus.Cancelled, stored.Status);
        Assert.IsNotNull(stored.CompletedAtUtc);
        Assert.IsNull(stored.StartedAtUtc, "A run cancelled before it started must not be marked as started.");
        Assert.IsEmpty(await verification.ScheduleVersions.ToListAsync(), "Cancelling must not keep any candidate.");
    }

    [TestMethod]
    public void ScheduleRunWorker_TellsOperatorCancellationApartFromShutdownAndFailure()
    {
        var predicate = typeof(ScheduleRunWorker).GetMethod("WasCancelledByOperator",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool Evaluate(Exception exception, CancellationToken solving, CancellationToken stopping) =>
            (bool)predicate.Invoke(null, [exception, solving, stopping])!;

        Assert.IsTrue(Evaluate(new OperationCanceledException(), cancelled.Token, CancellationToken.None));
        Assert.IsFalse(Evaluate(new InvalidOperationException(), cancelled.Token, CancellationToken.None),
            "A real failure must still be reported as a failure.");
        Assert.IsFalse(Evaluate(new OperationCanceledException(), cancelled.Token, cancelled.Token),
            "Shutdown must be left to the restart recovery path, not recorded as cancelled.");
    }

    private static ScheduleRun QueuedRun(WorkspaceCode workspace) => new()
    {
        Workspace = workspace,
        Month = new DateOnly(2026, 8, 1),
        Status = ScheduleRunStatus.Queued,
        RequestedByName = "editor",
        CorrelationId = "cancel-test",
        TimeLimitSeconds = 300,
        WorkerCount = 4
    };

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
        var result = await new AuditQueryService(database).QueryAsync(
            new DateOnly(2026, 8, 13), new DateOnly(2026, 8, 13), null, null, null, null, null, actor);

        Assert.HasCount(1, result);
        Assert.AreEqual("Included", result[0].Action);
        Assert.AreEqual("Included", result[0].Technical.Action);
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

        var service = new ScheduleService(database, new ScheduleValidationService(database));
        var bytes = await service.ExportExternalCsvAsync(version.Id, Editor(WorkspaceCode.M));

        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        StringAssert.Contains(System.Text.Encoding.UTF8.GetString(bytes), "2026-08-03,LB09,小,2");
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "ExternalScheduleCsvDownloaded"));
    }

    [TestMethod]
    public async Task Schedule_CanBeUnadoptedAndRenamed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = actor.UserId };
        var version = Version(revision, new DateOnly(2026, 8, 1), "原班表名稱");
        database.Context.AddRange(revision, version, new AdoptedSchedule
        {
            Workspace = version.Workspace,
            Month = version.Month,
            ScheduleVersionId = version.Id,
            AdoptedByUserId = actor.UserId
        });
        await database.Context.SaveChangesAsync();

        var service = new ScheduleService(database, new ScheduleValidationService(database));
        await service.UnadoptAsync(version.Id, version.RevisionToken, actor);
        Assert.IsFalse(await database.Context.AdoptedSchedules.AnyAsync(x => x.ScheduleVersionId == version.Id));
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "ScheduleUnadopted"));

        await using var afterUnadopt = database.NewContext();
        var token = await afterUnadopt.ScheduleVersions.AsNoTracking()
            .Where(x => x.Id == version.Id).Select(x => x.RevisionToken).SingleAsync();
        await service.RenameAsync(version.Id, "  新班表名稱  ", token, actor);
        await using var afterRename = database.NewContext();
        Assert.AreEqual("新班表名稱", await afterRename.ScheduleVersions.AsNoTracking()
            .Where(x => x.Id == version.Id).Select(x => x.Name).SingleAsync());
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "ScheduleRenamed"));
    }

    [TestMethod]
    public async Task ScheduleDetail_SplitsMultipleCollectionQueries()
    {
        await using var database = await TestDatabase.CreateAsync(throwOnMultipleCollectionInclude: true);
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = Guid.NewGuid() };
        revision.RestIntervals.Add(new RestIntervalEntity
        {
            Start = new DateOnly(2026, 7, 6),
            End = new DateOnly(2026, 8, 30),
            NationalHolidays = [new NationalHoliday { Date = new DateOnly(2026, 8, 8) }]
        });
        var version = Version(revision, new DateOnly(2026, 8, 1), "M 班表");
        version.Employees.Add(new ScheduleEmployeeSnapshot
        {
            EmployeeCode = "M001",
            Name = "王小明",
            Affiliation = "LB01",
            OpeningRest = 7,
            Assignments = [new ScheduleAssignment { Date = version.Month, Kind = "Rest" }]
        });
        version.ExternalAssignments.Add(new ExternalAssignment
        {
            Date = version.Month,
            Station = "LB09",
            Shift = "Early",
            Count = 1
        });
        database.Context.AddRange(revision, version);
        await database.Context.SaveChangesAsync();

        var service = new ScheduleService(database, new ScheduleValidationService(database));
        var detail = await service.GetAsync(version.Id, Editor(WorkspaceCode.M));

        Assert.AreEqual(version.Id, detail.Version.Id);
        Assert.HasCount(1, detail.Employees);
        Assert.HasCount(46, ScheduleCsv.MonthlyHeaders);
        Assert.HasCount(46, detail.Employees[0].MonthlyCsvValues);
        Assert.AreEqual("R", detail.Employees[0].MonthlyCsvValues[8]);
        Assert.HasCount(1, detail.ExternalAssignments);
        Assert.AreEqual(8, detail.IntervalStats.Single().Rest);
        Assert.AreEqual(1, detail.IntervalStats.Single().RequiredSpecialRest);
        Assert.AreEqual(1, detail.Coverage.Single(x => x.Date == version.Month && x.Station == "LB09" && x.Shift == "Early").External);
        var lb01Early = detail.Coverage.Single(x => x.Date == version.Month && x.Station == "LB01" && x.Shift == "Early");
        Assert.AreEqual(1, lb01Early.Maximum);
        Assert.IsFalse(lb01Early.AllowsMultiple);
    }

    [TestMethod]
    public async Task TSchedule_HidesAbilityFromViewersAndDownload()
    {
        await using var database = await TestDatabase.CreateAsync();
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = Guid.NewGuid() };
        var version = Version(revision, new DateOnly(2026, 8, 1), "T 班表");
        version.Workspace = WorkspaceCode.T;
        version.Employees.Add(new ScheduleEmployeeSnapshot
        {
            EmployeeCode = "T001",
            Name = "王小明",
            Affiliation = "號誌",
            Ability = 5,
            MonthlyShift = "Early"
        });
        database.Context.AddRange(revision, version);
        await database.Context.SaveChangesAsync();
        var service = new ScheduleService(database, new ScheduleValidationService(database));
        var viewer = new ActorContext(Guid.NewGuid(), "viewer", false, new HashSet<WorkspaceCode>(), "viewer-test");

        var viewerDetail = await service.GetAsync(version.Id, viewer);
        Assert.IsNull(viewerDetail.Employees.Single().Ability);
        Assert.AreEqual("", viewerDetail.Employees.Single().MonthlyCsvValues[4]);
        Assert.AreEqual(5, (await service.GetAsync(version.Id, Editor(WorkspaceCode.T))).Employees.Single().Ability);

        var csv = System.Text.Encoding.UTF8.GetString(await service.ExportCsvAsync(version.Id, viewer));
        Assert.IsTrue(csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Split(',').Contains("能力"));
        Assert.IsFalse(csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Split(',').Contains("萬年班表"));
        Assert.HasCount(45, csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Split(','));
    }

    [TestMethod]
    public async Task ScheduleEdit_AllowsLeaveRestWithoutRequestedRestMarker()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.M);
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = actor.UserId };
        var version = Version(revision, new DateOnly(2026, 8, 1), "M 班表");
        var assignment = new ScheduleAssignment { Date = version.Month, Kind = "Rest" };
        version.Employees.Add(new ScheduleEmployeeSnapshot
        {
            EmployeeCode = "M001",
            Name = "王小明",
            Affiliation = "LB01",
            Assignments = [assignment]
        });
        database.Context.AddRange(revision, version);
        await database.Context.SaveChangesAsync();

        var service = new ScheduleService(database, new ScheduleValidationService(database));
        var detail = await service.UpdateAssignmentAsync(version.Id, assignment.Id, "LeaveRest", false,
            null, null, null, null, null, version.RevisionToken, actor);

        var edited = detail.Assignments.Single();
        Assert.AreEqual("LeaveRest", edited.Kind);
        Assert.IsFalse(edited.RequestedRest);
    }

    [TestMethod]
    public async Task TScheduleMonthlyShift_CanBeUpdatedAndIsAudited()
    {
        await using var database = await TestDatabase.CreateAsync();
        var actor = Editor(WorkspaceCode.T);
        var revision = new ConfigurationRevision { Version = 1, CreatedByUserId = actor.UserId };
        var version = Version(revision, new DateOnly(2026, 8, 1), "T 班表");
        version.Workspace = WorkspaceCode.T;
        var employee = new ScheduleEmployeeSnapshot
        {
            EmployeeCode = "T001",
            Name = "王小明",
            Affiliation = "號誌",
            Ability = 5,
            MonthlyShift = "Early",
            Assignments = [new ScheduleAssignment { Date = version.Month, Kind = "Work", Shift = "Early" }]
        };
        version.Employees.Add(employee);
        database.Context.AddRange(revision, version);
        await database.Context.SaveChangesAsync();

        var service = new ScheduleService(database, new ScheduleValidationService(database));
        var detail = await service.UpdateMonthlyShiftAsync(version.Id, employee.Id, "Night", version.RevisionToken, actor);

        Assert.AreEqual("Night", detail.Employees.Single().MonthlyShift);
        await using var persisted = database.NewContext();
        Assert.AreEqual("Night", await persisted.ScheduleEmployeeSnapshots.AsNoTracking()
            .Where(x => x.Id == employee.Id).Select(x => x.MonthlyShift).SingleAsync());
        Assert.AreEqual(1, await database.Context.AuditLogs.CountAsync(x => x.Action == "ScheduleEmployeeMonthlyShiftUpdated"));
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
            Workspace = WorkspaceCode.M,
            Month = september,
            DemandDraftId = Guid.NewGuid(),
            ConfigurationRevisionId = revision.Id,
            RequestedByUserId = actor.UserId,
            RequestedByName = actor.UserName,
            InputSnapshotJson = System.Text.Json.JsonSerializer.Serialize(input,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        };
        var version = Version(revision, september, "候選");
        version.SourceRun = run;
        version.Employees.Add(new()
        {
            EmployeeCode = "M001",
            Name = "王小明",
            Affiliation = "LB01",
            Assignments = currentAssignments.Select(pair => new ScheduleAssignment
            {
                Date = pair.Key,
                Kind = pair.Value.Kind!.Value.ToString(),
                Station = pair.Value.Station,
                Shift = pair.Value.Shift?.ToString()
            }).ToList()
        });
        database.Context.AddRange(revision, run, version);
        await database.Context.SaveChangesAsync();

        var result = await new ScheduleValidationService(database).ValidateAsync(version.Id, actor);

        Assert.IsFalse(result.Issues.Any(x => x.RuleName == "連續七日至少一日一般 R" &&
            x.EmployeeCode == "M001" && x.Date is { Day: 1 or 2 }));
        Assert.IsTrue(result.Issues.Where(x => x.RuleName == "連續七日至少一日一般 R").All(x => x.IsLaborLawViolation));
        Assert.IsTrue(result.Issues.Where(x => x.RuleName != "連續七日至少一日一般 R" && x.RuleName != "兩班間至少十一小時")
            .All(x => !x.IsLaborLawViolation));
    }

    [TestMethod]
    public async Task ApplicationServices_RejectForgedAnonymousActor()
    {
        await using var database = await TestDatabase.CreateAsync();
        var anonymous = new ActorContext(Guid.Empty, "anonymous", true,
            new HashSet<WorkspaceCode> { WorkspaceCode.M, WorkspaceCode.T }, "anonymous-test");
        var month = new DateOnly(2026, 8, 1);

        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new CommonConfigurationService(database).GetCurrentAsync(anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new EmployeeService(database).ListAsync(WorkspaceCode.M, anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => DemandService(database).GetAsync(WorkspaceCode.M, month, anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new ScheduleRunService(database, new ScheduleRunQueue()).ListAsync(WorkspaceCode.M, month, anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new ScheduleValidationService(database).ValidateAsync(Guid.NewGuid(), anonymous));
        await Assert.ThrowsExactlyAsync<ForbiddenOperationException>(() => new AuditQueryService(database).QueryAsync(null, null, null, null, null, null, null, anonymous));
    }

    [TestMethod]
    public void AuditPresentation_LabelsKnownActions()
    {
        foreach (var option in AuditPresentation.ActionOptions())
            Assert.IsFalse(string.IsNullOrWhiteSpace(option.Label));
    }

    [TestMethod]
    public void AuditPresentation_ScheduleAssignment_ShowsScheduleEmployeeDateAndShiftChange()
    {
        var before = """{"month":"2026-08-01","scheduleName":"候選 1","employeeCode":"M001","name":"王小明","date":"2026-08-18","kind":"Work","shift":"Early","station":"LB01","requestedRest":false}""";
        var after = """{"month":"2026-08-01","scheduleName":"候選 1","employeeCode":"M001","name":"王小明","date":"2026-08-18","kind":"SpecialRest","shift":null,"station":null,"requestedRest":false}""";
        var row = new AuditLog
        {
            ActorName = "admin",
            Action = "ScheduleAssignmentUpdated",
            Workspace = WorkspaceCode.M,
            ResourceType = "ScheduleAssignment",
            ResourceId = Guid.NewGuid().ToString(),
            BeforeJson = before,
            AfterJson = after,
            CorrelationId = "test"
        };

        var dto = AuditPresentation.Format(row);

        StringAssert.Contains(dto.TargetSummary, "2026-08");
        StringAssert.Contains(dto.TargetSummary, "班表「候選 1」");
        StringAssert.Contains(dto.TargetSummary, "M001");
        StringAssert.Contains(dto.TargetSummary, "8/18");
        StringAssert.Contains(dto.ReadableSummary, "班表「候選 1」");
        StringAssert.Contains(dto.ReadableSummary, "M001");
        StringAssert.Contains(dto.ReadableSummary, "→");
        Assert.IsTrue(dto.Changes.Any(x => x.Label == "日格" && x.Before == "上班" && x.After == "R1"));
    }

    [TestMethod]
    public async Task AuditQuery_FiltersBySessionId()
    {
        await using var database = await TestDatabase.CreateAsync();
        var sessionId = Guid.NewGuid();
        database.Context.AuditLogs.AddRange(
            new AuditLog
            {
                AtUtc = DateTimeOffset.UtcNow,
                AtUtcTicks = DateTimeOffset.UtcNow.UtcTicks,
                ActorName = "admin",
                Action = "LoginSucceeded",
                ResourceType = "Authentication",
                ResourceId = "auth",
                SessionId = sessionId,
                CorrelationId = "test-1"
            },
            new AuditLog
            {
                AtUtc = DateTimeOffset.UtcNow,
                AtUtcTicks = DateTimeOffset.UtcNow.UtcTicks,
                ActorName = "admin",
                Action = "ScheduleAssignmentUpdated",
                ResourceType = "ScheduleAssignment",
                ResourceId = Guid.NewGuid().ToString(),
                SessionId = sessionId,
                CorrelationId = "test-2"
            },
            new AuditLog
            {
                AtUtc = DateTimeOffset.UtcNow,
                AtUtcTicks = DateTimeOffset.UtcNow.UtcTicks,
                ActorName = "other",
                Action = "LoginSucceeded",
                ResourceType = "Authentication",
                ResourceId = "auth",
                SessionId = Guid.NewGuid(),
                CorrelationId = "test-3"
            });
        await database.Context.SaveChangesAsync();

        var actor = new ActorContext(Guid.NewGuid(), "admin", true, new HashSet<WorkspaceCode>(), "audit-test");
        var result = await new AuditQueryService(database).QueryAsync(null, null, null, null, null, sessionId, null, actor);

        Assert.HasCount(2, result);
        Assert.IsTrue(result.All(x => x.SessionId == sessionId));
    }

    [TestMethod]
    public async Task ServiceSupport_AddAudit_PersistsSessionIdFromActor()
    {
        await using var database = await TestDatabase.CreateAsync();
        var sessionId = Guid.NewGuid();
        var actor = Editor(WorkspaceCode.M, sessionId);
        var service = new EmployeeService(database);
        await service.SaveAsync(new SaveEmployeeCommand(null, WorkspaceCode.M, "M001", "王小明", "LB01", null, null, null), actor);

        var audit = await database.Context.AuditLogs.SingleAsync();
        Assert.AreEqual(sessionId, audit.SessionId);
        Assert.AreEqual(actor.UserId, audit.ActorUserId);
    }

    private static ActorContext Editor(WorkspaceCode workspace, Guid? sessionId = null) =>
        new(Guid.NewGuid(), "editor", false, new HashSet<WorkspaceCode> { workspace }, Guid.NewGuid().ToString("N"), sessionId);

    private static DemandService DemandService(TestDatabase database) =>
        new(database);

    private static EmployeeDemandSubmissionService SubmissionService(TestDatabase database) =>
        new(database, DemandService(database));

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

    private sealed class FakeNavigationManager : NavigationManager
    {
        public string? Target { get; private set; }

        public FakeNavigationManager(string baseUri) => Initialize(baseUri, baseUri);

        protected override void NavigateToCore(string uri, bool forceLoad) => Target = uri;

        protected override void NavigateToCore(string uri, NavigationOptions options) => Target = uri;
    }

    private sealed class TestDatabase : IDbContextFactory<NtmcDbContext>, IAsyncDisposable
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

        public NtmcDbContext CreateDbContext() => NewContext();

        public ValueTask<NtmcDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(NewContext());

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class OptionsDbContextFactory(DbContextOptions<NtmcDbContext> options) : IDbContextFactory<NtmcDbContext>
    {
        public NtmcDbContext CreateDbContext() => new(options);

        public ValueTask<NtmcDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }
}
