using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Infrastructure.Services;
using NtmcScheduler.Solvers;

namespace NtmcScheduler.Infrastructure.Background;

public sealed class ScheduleRunWorker(
    ScheduleRunQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduleRunWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        await foreach (var runId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(runId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Schedule run {RunId} failed.", runId);
                await MarkFailedAsync(runId, exception, stoppingToken);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var pending = await db.ScheduleRuns.Where(x => x.Status == ScheduleRunStatus.Queued || x.Status == ScheduleRunStatus.Running).ToListAsync(cancellationToken);
        foreach (var run in pending)
        {
            run.Status = ScheduleRunStatus.Queued;
            run.StartedAtUtc = null;
        }
        await SaveChangesWithSqliteRetryAsync(db, cancellationToken);
        foreach (var run in pending) await queue.QueueAsync(run.Id, cancellationToken);
    }

    private async Task ProcessAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var run = await db.ScheduleRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null || run.Status != ScheduleRunStatus.Queued) return;
        run.Status = ScheduleRunStatus.Running;
        run.StartedAtUtc = DateTimeOffset.UtcNow;
        await SaveChangesWithSqliteRetryAsync(db, cancellationToken);

        var input = JsonSerializer.Deserialize<ScheduleInput>(run.InputSnapshotJson, ServiceSupport.JsonOptions)
            ?? throw new InvalidOperationException("Run input snapshot is invalid.");
        var options = new SolverOptions
        {
            RandomSeed = run.RandomSeed,
            WorkerCount = run.WorkerCount,
            TimeLimit = TimeSpan.FromSeconds(run.TimeLimitSeconds)
        };
        if (run.Workspace == WorkspaceCode.M)
        {
            var result = string.IsNullOrWhiteSpace(run.PerpetualScheduleJson)
                ? MSolver.Solve(input, options, cancellationToken)
                : MSolver.Solve(input, JsonSerializer.Deserialize<MPerpetualSchedule>(run.PerpetualScheduleJson, ServiceSupport.JsonOptions)!, options, cancellationToken);
            await StoreMResultAsync(db, run, input.DemandMonth, result, cancellationToken);
        }
        else
        {
            var result = TSolver.Solve(input, options, cancellationToken);
            await StoreTResultAsync(db, run, input.DemandMonth, result, cancellationToken);
        }
    }

    private static async Task StoreMResultAsync(NtmcDbContext db, ScheduleRun run, MonthlySchedule demand, MSolveResult result, CancellationToken cancellationToken)
    {
        run.Status = Map(result.Status);
        run.Error = ErrorText(result.Errors);
        for (var index = 0; index < result.Candidates.Count; index++)
        {
            var candidate = result.Candidates[index];
            var version = SolverScheduleMapper.ToVersion(candidate.Schedule, WorkspaceCode.M, run.Id, index, run.Status,
                ConfigurationId(run), run.RequestedByUserId, demand, candidate.ExternalAssignments);
            version.WarningCount = candidate.Objectives.SelectMany(x => x.Components).Count(x => x.Value > 0);
            db.ScheduleVersions.Add(version);
        }
        await CompleteAsync(db, run, result.Candidates.Count, cancellationToken);
    }

    private static async Task StoreTResultAsync(NtmcDbContext db, ScheduleRun run, MonthlySchedule demand, TSolveResult result, CancellationToken cancellationToken)
    {
        run.Status = Map(result.Status);
        run.Error = ErrorText(result.Errors);
        for (var index = 0; index < result.Candidates.Count; index++)
        {
            var candidate = result.Candidates[index];
            var version = SolverScheduleMapper.ToVersion(candidate.Schedule, WorkspaceCode.T, run.Id, index, run.Status,
                ConfigurationId(run), run.RequestedByUserId, demand);
            version.WarningCount = candidate.Objectives.SelectMany(x => x.Components).Count(x => x.Value > 0);
            db.ScheduleVersions.Add(version);
        }
        await CompleteAsync(db, run, result.Candidates.Count, cancellationToken);
    }

    private static async Task CompleteAsync(NtmcDbContext db, ScheduleRun run, int candidateCount, CancellationToken cancellationToken)
    {
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        var actor = Actor(run);
        ServiceSupport.AddAudit(db, actor, "ScheduleRunCompleted", run.Workspace, "ScheduleRun", run.Id, null,
            new { run.Status, CandidateCount = candidateCount, run.Error });
        await SaveChangesWithSqliteRetryAsync(db, cancellationToken);
    }

    private static string? ErrorText(IReadOnlyList<InputError> errors) =>
        errors.Count == 0 ? null : string.Join("；", errors.Select(x => $"{x.Field}: {x.Message}"));

    private static Guid ConfigurationId(ScheduleRun run) => run.ConfigurationRevisionId
        ?? throw new InvalidOperationException("Run configuration snapshot reference is missing.");

    private static ScheduleRunStatus Map(SolveStatus status) => status switch
    {
        SolveStatus.Optimal => ScheduleRunStatus.Optimal,
        SolveStatus.TimeLimit => ScheduleRunStatus.TimeLimit,
        SolveStatus.Infeasible => ScheduleRunStatus.Infeasible,
        SolveStatus.InvalidInput => ScheduleRunStatus.InvalidInput,
        _ => ScheduleRunStatus.Failed
    };

    private async Task MarkFailedAsync(Guid runId, Exception exception, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var run = await db.ScheduleRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null) return;
        run.Status = ScheduleRunStatus.Failed;
        run.Error = "背景求解失敗，請提供關聯 ID 給系統管理者。";
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        ServiceSupport.AddAudit(db, Actor(run), "ScheduleRunFailed", run.Workspace, "ScheduleRun", run.Id, null,
            new { ExceptionType = exception.GetType().Name });
        await SaveChangesWithSqliteRetryAsync(db, cancellationToken);
    }

    private static async Task SaveChangesWithSqliteRetryAsync(NtmcDbContext db, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (
                attempt < 5 && exception.InnerException is SqliteException { SqliteErrorCode: 5 or 6 })
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << attempt)), cancellationToken);
            }
        }
    }

    private static ActorContext Actor(ScheduleRun run) => new(
        run.RequestedByUserId,
        run.RequestedByName,
        false,
        new HashSet<WorkspaceCode> { run.Workspace },
        run.CorrelationId,
        run.IpAddress,
        run.UserAgent);
}
