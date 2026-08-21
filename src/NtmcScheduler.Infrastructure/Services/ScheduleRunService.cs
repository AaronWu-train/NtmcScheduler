using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Background;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class ScheduleRunService(IDbContextFactory<NtmcDbContext> dbFactory, ScheduleRunQueue queue, IScheduleRunNotifier? notifier = null) : IScheduleRunService
{
    public IReadOnlyList<SolverRuleDefinitionDto> GetRules(WorkspaceCode workspace)
    {
        var hard = new[]
        {
            new SolverRuleDefinitionDto("DailyAssignment", "每日單一狀態", "每位在職人員每日必須恰好一個工作或休假狀態。", 0, true, null),
            new SolverRuleDefinitionDto("FixedAssignments", "固定格", "已填正常班、R、R1 與 X 必須保留。", 0, true, null),
            new SolverRuleDefinitionDto("EmploymentStart", "到職日期", "到職日前不建立班位，也不可存在固定日格。", 0, true, null),
            new SolverRuleDefinitionDto("LeaveRestLimit", "R休上限", "R休只可用於 R*，且不可超過每人本月上限。", 0, true, null),
            new SolverRuleDefinitionDto("MinimumRest", "工作間隔", "任兩次工作至少間隔 11 小時。", 0, true, null),
            new SolverRuleDefinitionDto("SevenDayRest", "七日一般 R", "每個連續七日視窗至少一天一般 R。", 0, true, null),
            new SolverRuleDefinitionDto("EightWeekQuota", "56 日 R/R1 額度", "每區間必須剛好 16R，R1 等於國定假日數。", 0, true, null)
        };
        if (workspace == WorkspaceCode.M)
            hard = hard.Concat([
                new("StationGroup", "車站群組", "未固定正常班只能排在所屬站的本月群組內。", 0, true, null),
                new("StationCoverage", "班位人數", "每個站班的內部與外援總人數須符合本月上下限。", 0, true, null),
                new("ExternalSupport", "外援使用範圍", "不允許站不建立外援；其他站的外援只補最低需求差額。", 0, true, null)
            ]).ToArray();
        var defaults = workspace == WorkspaceCode.M ? SolverRuleWeights.M : SolverRuleWeights.T;
        return hard.Concat(defaults.Select(pair => new SolverRuleDefinitionDto(pair.Key, RuleName(pair.Key), RuleDescription(pair.Key), RulePriority(workspace, pair.Key), false, pair.Value))).ToArray();
    }

    public async Task<ScheduleRunDto> QueueAsync(Guid demandId, Guid revisionToken, ScheduleRunOptions options, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var demand = await db.DemandDrafts.AsSplitQuery()
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.NonStandardShifts)
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.StandardShiftTimes)
            .Include(x => x.Employees).ThenInclude(x => x.Assignments)
            .Include(x => x.UploadedPreviousSchedule)
            .SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理後再求解。");
        if (options.TimeLimitSeconds <= 0 || options.WorkerCount <= 0 || options.SeedCount <= 0)
            throw new DomainValidationException("求解時限、worker 數與 seed 數都必須是正整數。");
        // Seeds run sequentially, so the ceiling is deployment wall time rather than memory: at the
        // limits one run occupies the single-reader queue for 4 x 600 seconds.
        if (options.TimeLimitSeconds > ScheduleRunOptions.MaxTimeLimitSeconds
            || options.WorkerCount > ScheduleRunOptions.MaxWorkerCount
            || options.SeedCount > ScheduleRunOptions.MaxSeedCount)
            throw new DomainValidationException($"求解時限最多 {ScheduleRunOptions.MaxTimeLimitSeconds} 秒、worker 數最多 {ScheduleRunOptions.MaxWorkerCount}、seed 數最多 {ScheduleRunOptions.MaxSeedCount}。");
        if (demand.Workspace == WorkspaceCode.T && options.SeedCount != 1)
            throw new DomainValidationException("T 只支援一個 seed。");
        var previous = await ResolvePreviousAsync(db, demand, cancellationToken);
        var input = new ScheduleInput(
            previous,
            SolverScheduleMapper.ToMonthlySchedule(demand),
            SolverScheduleMapper.ToRestIntervals(demand.ConfigurationRevision),
            SolverScheduleMapper.ToNonStandardShifts(demand.ConfigurationRevision),
            SolverScheduleMapper.ToStandardShiftTimes(demand.ConfigurationRevision),
            SolverScheduleMapper.ToMonthlySettings(demand));
        IReadOnlyDictionary<string, int> resolvedWeights;
        try { resolvedWeights = SolverRuleWeights.Resolve(demand.Workspace == WorkspaceCode.M, options.RuleWeights); }
        catch (ArgumentException) { throw new DomainValidationException("規則權重必須完整、不可為負數，且只能包含目前啟用的軟規則。"); }
        var snapshot = JsonSerializer.Serialize(input, ServiceSupport.JsonOptions);
        var perpetualScheduleJson = demand.PerpetualScheduleJson;
        if (demand.Workspace == WorkspaceCode.M && string.IsNullOrWhiteSpace(perpetualScheduleJson))
            perpetualScheduleJson = await db.MPerpetualScheduleTemplates.AsNoTracking().Where(x => x.Id == 1)
                .Select(x => x.ScheduleJson).SingleOrDefaultAsync(cancellationToken);
        var run = new ScheduleRun
        {
            Workspace = demand.Workspace,
            Month = demand.Month,
            DemandDraftId = demand.Id,
            ConfigurationRevisionId = demand.ConfigurationRevisionId,
            RequestedByUserId = actor.UserId,
            RequestedByName = actor.UserName,
            CorrelationId = actor.CorrelationId,
            SessionId = actor.SessionId,
            IpAddress = actor.IpAddress,
            UserAgent = actor.UserAgent,
            RandomSeed = RandomNumberGenerator.GetInt32(1, int.MaxValue),
            WorkerCount = options.WorkerCount,
            SeedCount = options.SeedCount,
            TimeLimitSeconds = options.TimeLimitSeconds,
            ProgramVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                ?? "unknown",
            InputSnapshotJson = snapshot,
            InputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))),
            PerpetualScheduleJson = perpetualScheduleJson
        };
        run.RuleWeightsJson = JsonSerializer.Serialize(resolvedWeights, ServiceSupport.JsonOptions);
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.ScheduleRuns.Add(run);
            ServiceSupport.AddAudit(db, actor, "ScheduleRunQueued", demand.Workspace, "ScheduleRun", run.Id, null,
                new { run.Month, run.InputHash, run.RandomSeed, run.WorkerCount, run.SeedCount, run.TimeLimitSeconds, RuleWeights = resolvedWeights });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await queue.QueueAsync(run.Id, cancellationToken);
        var dto = ToDto(run);
        if (notifier is not null) await notifier.NotifyAsync(dto, cancellationToken);
        return dto;
    }

    public async Task<IReadOnlyList<ScheduleRunDto>> ListAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireEditor(actor, workspace);
        month = new(month.Year, month.Month, 1);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db.ScheduleRuns.AsNoTracking().Where(x => x.Workspace == workspace && x.Month == month)
            .ToListAsync(cancellationToken);
        return runs.OrderByDescending(x => x.CreatedAtUtc).Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<ScheduleRunDto>> ListActiveAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var active = await db.ScheduleRuns.AsNoTracking()
            .Where(x => x.Status == ScheduleRunStatus.Queued || x.Status == ScheduleRunStatus.Running)
            .ToListAsync(cancellationToken);
        return active.OrderBy(x => x.CreatedAtUtc).Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<ScheduleRunDto>> ListRecentAsync(int count, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        if (count is < 1 or > 100) throw new DomainValidationException("查詢筆數必須介於 1 到 100。");
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db.ScheduleRuns.AsNoTracking().ToListAsync(cancellationToken);
        return runs.OrderByDescending(x => x.CreatedAtUtc).Take(count).Select(ToDto).ToArray();
    }

    public async Task CancelAsync(Guid runId, ActorContext actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.ScheduleRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new DomainValidationException("找不到求解工作。");
        ServiceSupport.RequireEditor(actor, run.Workspace);
        if (queue.CancellationFor(runId).IsCancellationRequested)
            throw new DomainValidationException("已經要求取消，請等待求解停止。");
        if (run.Status is not (ScheduleRunStatus.Queued or ScheduleRunStatus.Running) || !queue.Cancel(runId))
            throw new DomainValidationException("求解工作已經結束，無法取消。");
        // The worker owns the final status: it writes Cancelled once the solver has actually
        // stopped, and notifies the UI from there.
        ServiceSupport.AddAudit(db, actor, "ScheduleRunCancelled", run.Workspace, "ScheduleRun", run.Id, null,
            new { run.Month, CancelledFrom = run.Status });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<MonthlySchedule> ResolvePreviousAsync(NtmcDbContext db, DemandDraft demand, CancellationToken cancellationToken)
    {
        if (demand.PreviousSource == PreviousScheduleSource.Upload)
        {
            if (demand.UploadedPreviousSchedule is null)
                throw new DomainValidationException("上個月沒有已採用班表，請先上傳上月班表。");
            return JsonSerializer.Deserialize<MonthlySchedule>(demand.UploadedPreviousSchedule.ParsedScheduleJson, ServiceSupport.JsonOptions)
                ?? throw new DomainValidationException("previous schedule 快照無法讀取。");
        }
        if (demand.PreviousAdoptedScheduleVersionId is not { } versionId)
            throw new DomainValidationException("找不到選取的上月班表。");
        var version = await db.ScheduleVersions.AsNoTracking().Include(x => x.Employees).ThenInclude(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == versionId && !x.IsArchived, cancellationToken)
            ?? throw new DomainValidationException("選取的上月班表不存在或已封存。");
        return SolverScheduleMapper.ToMonthlySchedule(version);
    }

    internal static ScheduleRunDto ToDto(ScheduleRun run) => new(run.Id, run.Workspace, run.Month, run.Status, run.Error, run.CreatedAtUtc, run.CompletedAtUtc, run.TimeLimitSeconds, run.WorkerCount, run.SeedCount,
        string.IsNullOrWhiteSpace(run.RuleWeightsJson)
            ? (run.Workspace == WorkspaceCode.M ? SolverRuleWeights.M : SolverRuleWeights.T)
            : JsonSerializer.Deserialize<Dictionary<string, int>>(run.RuleWeightsJson, ServiceSupport.JsonOptions) ?? new Dictionary<string, int>(),
        DeserializeCandidates(run.ResultDetailsJson));

    private static int RulePriority(WorkspaceCode workspace, string key) => key switch
    {
        "RequestedRest" or "UnusedLeaveRest" => 1,
        "NonMonthlyShift" or "Attendance" or "Specialty" or "Ability" => 2,
        "MonthlyRest" or "SpecialRestBalance" when workspace == WorkspaceCode.T => 3,
        "WeekdayRestFairness" or "HolidayRestFairness" when workspace == WorkspaceCode.T => 5,
        _ when workspace == WorkspaceCode.M => 2,
        _ => 4
    };

    private static string RuleName(string key) => key switch
    {
        "RequestedRest" => "指定休假", "UnusedLeaveRest" => "未使用 R休", "ExternalStaffing" => "外援人力",
        "MonthlyRest" => "每月一般 R", "SpecialRestBalance" => "八週累積 R1 餘額", "WorkStreak" => "連續工作區段",
        "MixedShiftWorkStreak" => "工作區段混合班型", "NightRestEarly" => "夜休早", "NightRestAfternoon" => "夜休午",
        "ShiftChangeWithoutRest" => "未休假直接換班", "HolidayRestFairness" => "假日休假公平",
        "EarlyAfternoonImbalance" => "早小班差距", "NightShiftTarget" => "夜班目標", "NonMonthlyShift" => "月班別不一致",
        "Attendance" => "班組出勤不足", "Specialty" => "專業缺席", "Ability" => "高能力人員不足",
        "NightToEarlyRest" => "跨月夜轉早休假不足", "MonthBoundaryRestBalance" => "月交界休假不平衡",
        "WeekdayRestFairness" => "平日休假公平", _ => key
    };

    private static string RuleDescription(string key) => key switch
    {
        "RequestedRest" => "R* 最後未排成任何實際休假。", "UnusedLeaveRest" => "未用完每人輸入的 R休 上限。",
        "ExternalStaffing" => "允許站超過 70 人次與盡量不要站的外援人次。", "MonthlyRest" => "實際 R 與本月自訂基準的平方偏差。",
        "SpecialRestBalance" => "截至月底的 56 日累積 R1 餘額。", "WorkStreak" => "連續實際工作區段的長度品質。",
        _ => "依目前規格計算此軟規則的違反量。"
    };

    private static IReadOnlyList<ScheduleRunCandidateDto> DeserializeCandidates(string? json) => string.IsNullOrWhiteSpace(json)
        ? [] : JsonSerializer.Deserialize<List<ScheduleRunCandidateDto>>(json, ServiceSupport.JsonOptions) ?? [];
}
