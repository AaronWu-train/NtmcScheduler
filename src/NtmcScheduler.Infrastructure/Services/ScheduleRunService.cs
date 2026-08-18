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

public sealed class ScheduleRunService(NtmcDbContext db, ScheduleRunQueue queue, IScheduleRunNotifier? notifier = null) : IScheduleRunService
{
    public async Task<ScheduleRunDto> QueueAsync(Guid demandId, Guid revisionToken, ScheduleRunOptions options, ActorContext actor, CancellationToken cancellationToken = default)
    {
        var demand = await db.DemandDrafts.AsSplitQuery()
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.NonStandardShifts)
            .Include(x => x.Employees).ThenInclude(x => x.Assignments)
            .Include(x => x.UploadedPreviousSchedule)
            .SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到本月需求。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("本月需求已被其他人修改，請重新整理後再求解。");
        if (options.TimeLimitSeconds <= 0 || options.WorkerCount <= 0 || options.SeedCount <= 0)
            throw new DomainValidationException("求解時限、worker 數與 seed 數都必須是正整數。");
        if (demand.Workspace == WorkspaceCode.T && options.SeedCount != 1)
            throw new DomainValidationException("T 只支援一個 seed。");
        var previous = await ResolvePreviousAsync(demand, cancellationToken);
        var input = new ScheduleInput(
            previous,
            SolverScheduleMapper.ToMonthlySchedule(demand),
            SolverScheduleMapper.ToRestIntervals(demand.ConfigurationRevision),
            SolverScheduleMapper.ToNonStandardShifts(demand.ConfigurationRevision));
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
            ProgramVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            InputSnapshotJson = snapshot,
            InputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))),
            PerpetualScheduleJson = perpetualScheduleJson
        };
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.ScheduleRuns.Add(run);
            ServiceSupport.AddAudit(db, actor, "ScheduleRunQueued", demand.Workspace, "ScheduleRun", run.Id, null,
                new { run.Month, run.InputHash, run.RandomSeed, run.WorkerCount, run.SeedCount, run.TimeLimitSeconds });
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
        var runs = await db.ScheduleRuns.AsNoTracking().Where(x => x.Workspace == workspace && x.Month == month)
            .ToListAsync(cancellationToken);
        return runs.OrderByDescending(x => x.CreatedAtUtc).Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<ScheduleRunDto>> ListActiveAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        var active = await db.ScheduleRuns.AsNoTracking()
            .Where(x => x.Status == ScheduleRunStatus.Queued || x.Status == ScheduleRunStatus.Running)
            .ToListAsync(cancellationToken);
        return active.OrderBy(x => x.CreatedAtUtc).Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<ScheduleRunDto>> ListRecentAsync(int count, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        if (count is < 1 or > 100) throw new DomainValidationException("查詢筆數必須介於 1 到 100。");
        var runs = await db.ScheduleRuns.AsNoTracking().ToListAsync(cancellationToken);
        return runs.OrderByDescending(x => x.CreatedAtUtc).Take(count).Select(ToDto).ToArray();
    }

    private async Task<MonthlySchedule> ResolvePreviousAsync(DemandDraft demand, CancellationToken cancellationToken)
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

    internal static ScheduleRunDto ToDto(ScheduleRun run) => new(run.Id, run.Workspace, run.Month, run.Status, run.Error, run.CreatedAtUtc, run.CompletedAtUtc, run.TimeLimitSeconds, run.WorkerCount, run.SeedCount, DeserializeCandidates(run.ResultDetailsJson));

    private static IReadOnlyList<ScheduleRunCandidateDto> DeserializeCandidates(string? json) => string.IsNullOrWhiteSpace(json)
        ? [] : JsonSerializer.Deserialize<List<ScheduleRunCandidateDto>>(json, ServiceSupport.JsonOptions) ?? [];
}
