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

public sealed class ScheduleRunService(NtmcDbContext db, ScheduleRunQueue queue) : IScheduleRunService
{
    public async Task<ScheduleRunDto> QueueAsync(Guid demandId, Guid revisionToken, ActorContext actor, CancellationToken cancellationToken = default)
    {
        var demand = await db.DemandDrafts
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.RestIntervals).ThenInclude(x => x.NationalHolidays)
            .Include(x => x.ConfigurationRevision).ThenInclude(x => x.NonStandardShifts)
            .Include(x => x.Employees).ThenInclude(x => x.Assignments)
            .Include(x => x.UploadedPreviousSchedule)
            .SingleOrDefaultAsync(x => x.Id == demandId, cancellationToken)
            ?? throw new DomainValidationException("找不到 Demand。");
        ServiceSupport.RequireEditor(actor, demand.Workspace);
        if (demand.RevisionToken != revisionToken) throw new ConcurrencyConflictException("Demand 已被其他人修改，請重新整理後再求解。");
        var previous = await ResolvePreviousAsync(demand, cancellationToken);
        var input = new ScheduleInput(
            previous,
            SolverScheduleMapper.ToMonthlySchedule(demand),
            SolverScheduleMapper.ToRestIntervals(demand.ConfigurationRevision),
            SolverScheduleMapper.ToNonStandardShifts(demand.ConfigurationRevision));
        var snapshot = JsonSerializer.Serialize(input, ServiceSupport.JsonOptions);
        var run = new ScheduleRun
        {
            Workspace = demand.Workspace,
            Month = demand.Month,
            DemandDraftId = demand.Id,
            ConfigurationRevisionId = demand.ConfigurationRevisionId,
            RequestedByUserId = actor.UserId,
            RequestedByName = actor.UserName,
            CorrelationId = actor.CorrelationId,
            IpAddress = actor.IpAddress,
            UserAgent = actor.UserAgent,
            RandomSeed = 0,
            WorkerCount = demand.Workspace == WorkspaceCode.M ? 4 : 8,
            TimeLimitSeconds = 300,
            ProgramVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            InputSnapshotJson = snapshot,
            InputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))),
            PerpetualScheduleJson = demand.PerpetualScheduleJson
        };
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            db.ScheduleRuns.Add(run);
            ServiceSupport.AddAudit(db, actor, "ScheduleRunQueued", demand.Workspace, "ScheduleRun", run.Id, null,
                new { run.Month, run.InputHash, run.RandomSeed, run.WorkerCount, run.TimeLimitSeconds });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await queue.QueueAsync(run.Id, cancellationToken);
        return ToDto(run);
    }

    public async Task<IReadOnlyList<ScheduleRunDto>> ListAsync(WorkspaceCode workspace, DateOnly month, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireViewer(actor);
        month = new(month.Year, month.Month, 1);
        var runs = await db.ScheduleRuns.AsNoTracking().Where(x => x.Workspace == workspace && x.Month == month)
            .Select(x => new ScheduleRunDto(x.Id, x.Workspace, x.Month, x.Status, x.Error, x.CreatedAtUtc, x.CompletedAtUtc))
            .ToListAsync(cancellationToken);
        return runs.OrderByDescending(x => x.CreatedAtUtc).ToArray();
    }

    private async Task<MonthlySchedule> ResolvePreviousAsync(DemandDraft demand, CancellationToken cancellationToken)
    {
        if (demand.PreviousSource == PreviousScheduleSource.Upload)
        {
            if (demand.UploadedPreviousSchedule is null)
                throw new DomainValidationException("上個月沒有 ★ 班表，請先上傳 previous schedule。");
            return JsonSerializer.Deserialize<MonthlySchedule>(demand.UploadedPreviousSchedule.ParsedScheduleJson, ServiceSupport.JsonOptions)
                ?? throw new DomainValidationException("previous schedule 快照無法讀取。");
        }
        if (demand.PreviousAdoptedScheduleVersionId is not { } versionId)
            throw new DomainValidationException("找不到上個月的 ★ 班表。");
        var version = await db.ScheduleVersions.AsNoTracking().Include(x => x.Employees).ThenInclude(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == versionId && !x.IsArchived, cancellationToken)
            ?? throw new DomainValidationException("上個月的 ★ 班表不存在或已封存。");
        return SolverScheduleMapper.ToMonthlySchedule(version);
    }

    internal static ScheduleRunDto ToDto(ScheduleRun run) => new(run.Id, run.Workspace, run.Month, run.Status, run.Error, run.CreatedAtUtc, run.CompletedAtUtc);
}
