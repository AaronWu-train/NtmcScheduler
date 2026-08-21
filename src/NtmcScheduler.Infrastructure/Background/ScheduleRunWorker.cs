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
    ILogger<ScheduleRunWorker> logger,
    IScheduleRunNotifier? notifier = null) : BackgroundService
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
            finally
            {
                queue.Release(runId);
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

    private async Task ProcessAsync(Guid runId, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmcDbContext>();
        var run = await db.ScheduleRuns.SingleOrDefaultAsync(x => x.Id == runId, stoppingToken);
        if (run is null || run.Status != ScheduleRunStatus.Queued) return;

        // Persistence keeps using stoppingToken so an operator cancellation still records a final
        // status; only the solver itself observes the combined token.
        using var solving = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, queue.CancellationFor(runId));
        if (solving.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            await MarkCancelledAsync(db, run, stoppingToken);
            return;
        }
        run.Status = ScheduleRunStatus.Running;
        run.StartedAtUtc = DateTimeOffset.UtcNow;
        await SaveChangesWithSqliteRetryAsync(db, stoppingToken);
        await NotifyAsync(run, stoppingToken);

        var input = JsonSerializer.Deserialize<ScheduleInput>(run.InputSnapshotJson, ServiceSupport.JsonOptions)
            ?? throw new InvalidOperationException("Run input snapshot is invalid.");
        var options = new SolverOptions
        {
            RandomSeed = run.RandomSeed,
            WorkerCount = run.WorkerCount,
            TimeLimit = TimeSpan.FromSeconds(run.TimeLimitSeconds),
            RuleWeights = string.IsNullOrWhiteSpace(run.RuleWeightsJson) ? null
                : JsonSerializer.Deserialize<Dictionary<string, int>>(run.RuleWeightsJson, ServiceSupport.JsonOptions)
        };
        try
        {
            if (run.Workspace.IsStation())
            {
                var result = SolveMPortfolio(input, run, options, solving.Token);
                await StoreMResultAsync(db, run, input.DemandMonth, input.MonthlySettings, result, stoppingToken);
            }
            else
            {
                var result = TSolver.Solve(input, options, solving.Token);
                await StoreTResultAsync(db, run, input.DemandMonth, input.MonthlySettings, result, stoppingToken);
            }
        }
        catch (Exception exception) when (WasCancelledByOperator(exception, solving.Token, stoppingToken))
        {
            await MarkCancelledAsync(db, run, stoppingToken);
        }
    }

    private static bool WasCancelledByOperator(Exception exception, CancellationToken solving, CancellationToken stoppingToken) =>
        solving.IsCancellationRequested && !stoppingToken.IsCancellationRequested &&
        exception is OperationCanceledException;

    private async Task MarkCancelledAsync(NtmcDbContext db, ScheduleRun run, CancellationToken cancellationToken)
    {
        run.Status = ScheduleRunStatus.Cancelled;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        await SaveChangesWithSqliteRetryAsync(db, cancellationToken);
        await NotifyAsync(run, cancellationToken);
    }

    private async Task StoreMResultAsync(NtmcDbContext db, ScheduleRun run, MonthlySchedule demand, MonthlySchedulingSettings? settings, MSolveResult result, CancellationToken cancellationToken)
    {
        run.Status = Map(result.Status);
        run.Error = ErrorText(result.Errors);
        run.ResultDetailsJson = JsonSerializer.Serialize(result.Candidates.Select((candidate, index) => ToDto(index + 1, candidate.Objectives)), ServiceSupport.JsonOptions);
        for (var index = 0; index < result.Candidates.Count; index++)
        {
            var candidate = result.Candidates[index];
            var version = SolverScheduleMapper.ToVersion(candidate.Schedule, run.Workspace, run.Id, index, run.Status,
                ConfigurationId(run), run.RequestedByUserId, demand, candidate.ExternalAssignments, settings);
            version.RuleWeightsJson = run.RuleWeightsJson;
            version.WarningCount = candidate.Objectives.SelectMany(x => x.Components).Count(x => x.Value > 0);
            db.ScheduleVersions.Add(version);
        }
        await CompleteAsync(db, run, result.Candidates.Count, cancellationToken);
        await NotifyAsync(run, cancellationToken);
    }

    private async Task StoreTResultAsync(NtmcDbContext db, ScheduleRun run, MonthlySchedule demand, MonthlySchedulingSettings? settings, TSolveResult result, CancellationToken cancellationToken)
    {
        run.Status = Map(result.Status);
        run.Error = ErrorText(result.Errors);
        run.ResultDetailsJson = JsonSerializer.Serialize(result.Candidates.Select((candidate, index) => ToDto(index + 1, candidate.Objectives)), ServiceSupport.JsonOptions);
        for (var index = 0; index < result.Candidates.Count; index++)
        {
            var candidate = result.Candidates[index];
            var version = SolverScheduleMapper.ToVersion(candidate.Schedule, WorkspaceCode.T, run.Id, index, run.Status,
                ConfigurationId(run), run.RequestedByUserId, demand, monthlySettings: settings);
            version.RuleWeightsJson = run.RuleWeightsJson;
            version.WarningCount = candidate.Objectives.SelectMany(x => x.Components).Count(x => x.Value > 0);
            db.ScheduleVersions.Add(version);
        }
        await CompleteAsync(db, run, result.Candidates.Count, cancellationToken);
        await NotifyAsync(run, cancellationToken);
    }

    private static async Task CompleteAsync(NtmcDbContext db, ScheduleRun run, int candidateCount, CancellationToken cancellationToken)
    {
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        var actor = Actor(run);
        ServiceSupport.AddAudit(db, actor, "ScheduleRunCompleted", run.Workspace, "ScheduleRun", run.Id, null,
            new { run.Month, run.Status, CandidateCount = candidateCount, run.Error });
        await SaveChangesWithSqliteRetryAsync(db, cancellationToken);
    }

    private MSolveResult SolveMPortfolio(ScheduleInput input, ScheduleRun run, SolverOptions options, CancellationToken cancellationToken)
    {
        var template = string.IsNullOrWhiteSpace(run.PerpetualScheduleJson) ? null
            : JsonSerializer.Deserialize<MPerpetualSchedule>(run.PerpetualScheduleJson, ServiceSupport.JsonOptions);
        if (template?.Patterns.Count == 0) template = null;
        // Seeds run one after another so a portfolio never multiplies the memory and CPU of a single
        // solve. TimeLimit stays per seed, which makes the wall time SeedCount x TimeLimit.
        MSolveResult? best = null;
        for (var index = 0; index < run.SeedCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seeded = options with { RandomSeed = unchecked(run.RandomSeed + index) };
            var result = template is null
                ? MSolver.Solve(input, seeded, cancellationToken)
                : MSolver.Solve(input, template, seeded, cancellationToken);
            if (best is null || Compare(result, best) < 0) best = result;
        }
        return best ?? throw new InvalidOperationException("A run must have at least one seed.");
    }

    private static int Compare(MSolveResult left, MSolveResult right)
    {
        if (left.Candidates.Count == 0) return right.Candidates.Count == 0 ? 0 : 1;
        if (right.Candidates.Count == 0) return -1;
        var leftScores = left.Candidates[0].Objectives;
        var rightScores = right.Candidates[0].Objectives;
        for (var index = 0; index < Math.Min(leftScores.Count, rightScores.Count); index++)
        {
            var comparison = leftScores[index].Value.CompareTo(rightScores[index].Value);
            if (comparison != 0) return comparison;
        }
        return leftScores.Count.CompareTo(rightScores.Count);
    }

    private Task NotifyAsync(ScheduleRun run, CancellationToken cancellationToken) =>
        notifier?.NotifyAsync(ScheduleRunService.ToDto(run), cancellationToken) ?? Task.CompletedTask;

    private static string? ErrorText(IReadOnlyList<InputError> errors) =>
        errors.Count == 0 ? null : string.Join("；", errors.Select(x => $"{x.Field}: {x.Message}"));

    private static ScheduleRunCandidateDto ToDto(int number, IReadOnlyList<ObjectiveScore> objectives) => new(number,
        objectives.Select(objective => new ObjectiveScoreDto(objective.Priority, objective.Name, objective.Value,
            objective.Components.Select(component => new ObjectiveComponentDto(component.Name, component.Value, component.Weight)).ToArray())).ToArray());

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
            new { run.Month, ExceptionType = exception.GetType().Name });
        await SaveChangesWithSqliteRetryAsync(db, cancellationToken);
        await NotifyAsync(run, cancellationToken);
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
        run.SessionId,
        run.IpAddress,
        run.UserAgent);
}
