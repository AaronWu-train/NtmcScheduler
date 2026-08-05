using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Time;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;
using FixedEventType = NtmScheduler.Core.Abstractions.Dtos.FixedEventType;

namespace NtmScheduler.Infrastructure.Background;

/// <summary>
/// Polls Queued ScheduleRuns one at a time, builds SolveRequest from DB, calls ISolveService.
/// On startup, residual Running rows are reset to Queued for snapshot replay (AC-23).
/// </summary>
public sealed class ScheduleRunWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduleRunWorker> _logger;

    public ScheduleRunWorker(IServiceScopeFactory scopeFactory, ILogger<ScheduleRunWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResetRunningToQueuedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(stoppingToken);
                if (!processed)
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ScheduleRunWorker 未預期錯誤");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ResetRunningToQueuedAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmDbContext>();
        var running = await db.ScheduleRuns
            .Where(r => r.Status == ScheduleRunStatus.Running)
            .ToListAsync(ct);
        if (running.Count == 0)
            return;

        foreach (var run in running)
            run.Status = ScheduleRunStatus.Queued;

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("已將 {Count} 筆 Running 重設為 Queued", running.Count);
    }

    private async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NtmDbContext>();
        var solver = scope.ServiceProvider.GetRequiredService<ISolveService>();

        var run = await db.ScheduleRuns
            .Where(r => r.Status == ScheduleRunStatus.Queued)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (run is null)
            return false;

        run.Status = ScheduleRunStatus.Running;
        run.ProgressJson = JsonSerializer.Serialize(new
        {
            currentRuleId = (string?)null,
            completedRuleIds = Array.Empty<string>(),
            message = "開始求解"
        }, JsonOptions);
        await db.SaveChangesAsync(ct);

        try
        {
            var request = await BuildSolveRequestAsync(db, run, ct);
            var lastFlush = DateTime.MinValue;
            var progress = new Progress<SolveProgress>(p =>
            {
                var now = TaipeiTime.Now;
                if ((now - lastFlush).TotalSeconds < 2)
                    return;
                lastFlush = now;
                try
                {
                    run.ProgressJson = JsonSerializer.Serialize(new
                    {
                        currentRuleId = p.CurrentRuleId,
                        completedRuleIds = p.CompletedRuleIds,
                        message = p.Message,
                        objectiveBound = p.ObjectiveBound
                    }, JsonOptions);
                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "寫入 Run {RunId} 進度失敗", run.Id);
                }
            });

            var result = await solver.SolveAsync(request, progress, ct);
            await ApplyResultAsync(db, run, result, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId} 求解失敗", run.Id);
            run.Status = ScheduleRunStatus.Failed;
            run.ProgressJson = JsonSerializer.Serialize(new { message = ex.Message }, JsonOptions);
            run.ResultJson = JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    private static async Task<SolveRequest> BuildSolveRequestAsync(
        NtmDbContext db, ScheduleRun run, CancellationToken ct)
    {
        var month = YearMonth.Parse(run.TargetMonth);
        var period = ScheduleCalendar.CreatePeriod(month);

        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.Unit == run.Unit)
            .OrderBy(e => e.Id)
            .Select(e => new EmployeeInfo(e.Id, e.Name, e.Unit, e.HomeStation, e.Specialty, e.Ability))
            .ToListAsync(ct);

        var cycles = await db.ScheduleCycles.AsNoTracking()
            .Where(c => c.Start <= period.RangeEnd && c.End >= period.FirstDay)
            .Select(c => new CycleInfo(c.Start, c.End, c.RequiredR, c.RequiredR1))
            .ToListAsync(ct);

        var empIds = employees.Select(e => e.Id).ToList();
        var xEvents = await db.FixedEvents.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId) && e.Type == FixedEventType.X
                        && e.Start != null && e.End != null)
            .Select(e => new XEvent(e.EmployeeId, e.Start!.Value, e.End!.Value, e.Description ?? ""))
            .ToListAsync(ct);

        var rStarRows = await db.FixedEvents.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId) && e.Type == FixedEventType.RStar && e.Date != null)
            .Select(e => new { e.EmployeeId, Date = e.Date!.Value })
            .ToListAsync(ct);
        var rStars = rStarRows
            .Select(e => (e.EmployeeId, e.Date))
            .ToList();

        var softRules = await db.RuleSettings.AsNoTracking()
            .Where(r => r.Unit == run.Unit && r.Priority >= 1 && r.Enabled)
            .OrderBy(r => r.Priority).ThenBy(r => r.Order)
            .Select(r => new SoftRuleSpec(r.RuleId, r.Order, r.Enabled, r.ParametersJson))
            .ToListAsync(ct);

        IReadOnlyDictionary<string, ShiftType>? monthly = null;
        if (run.Unit == Unit.T)
        {
            monthly = await db.EmployeeMonthlyShifts.AsNoTracking()
                .Where(s => s.Month == month.ToString())
                .ToDictionaryAsync(s => s.EmployeeId, s => s.Shift, ct);
        }

        return new SolveRequest
        {
            Unit = run.Unit,
            Period = period,
            Employees = employees,
            Cycles = cycles,
            Histories = new Dictionary<string, EmployeeHistory>(),
            XEvents = xEvents,
            RStarRequests = rStars,
            SoftRules = softRules,
            MonthlyShifts = monthly,
            Seed = run.Seed
        };
    }

    private static async Task ApplyResultAsync(
        NtmDbContext db, ScheduleRun run, SolveResult result, CancellationToken ct)
    {
        run.ScheduleStatus = result.ScheduleStatus switch
        {
            ScheduleStatus.Feasible => ScheduleStatusCode.Feasible,
            ScheduleStatus.Infeasible => ScheduleStatusCode.Infeasible,
            ScheduleStatus.InvalidInput => ScheduleStatusCode.InvalidInput,
            _ => null
        };
        run.OptimizationStatus = result.OptimizationStatus switch
        {
            OptimizationStatus.Optimal => OptimizationStatusCode.Optimal,
            OptimizationStatus.TimeLimit => OptimizationStatusCode.TimeLimit,
            _ => null
        };

        var candidates = result.Candidates.ToList();
        if (result.ShortageAnalysis is { } shortage)
            candidates.Add(shortage);

        run.CandidateCount = result.Candidates.Count(c => !c.IsShortageAnalysis);
        run.ShortageAnalysisAvailable = result.ShortageAnalysisAvailable;
        run.ResultJson = JsonSerializer.Serialize(new
        {
            scheduleStatus = result.ScheduleStatus.ToString(),
            optimizationStatus = result.OptimizationStatus?.ToString(),
            candidateCount = run.CandidateCount,
            shortageAnalysisAvailable = run.ShortageAnalysisAvailable,
            error = result.ErrorMessage,
            tConflict = result.TConflictSummary?.Message
        }, JsonOptions);

        if (!string.IsNullOrEmpty(result.ErrorMessage) && result.Candidates.Count == 0 && result.ShortageAnalysis is null)
        {
            run.Status = ScheduleRunStatus.Failed;
            run.ProgressJson = JsonSerializer.Serialize(new { message = result.ErrorMessage }, JsonOptions);
            await db.SaveChangesAsync(ct);
            return;
        }

        run.Status = ScheduleRunStatus.Completed;
        run.ProgressJson = JsonSerializer.Serialize(new
        {
            currentRuleId = (string?)null,
            completedRuleIds = Array.Empty<string>(),
            message = "完成"
        }, JsonOptions);

        foreach (var c in candidates)
        {
            var entity = new CandidateSolution
            {
                RunId = run.Id,
                Index = c.Index,
                IsShortageAnalysis = c.IsShortageAnalysis,
                MetricsJson = JsonSerializer.Serialize(new
                {
                    violations = c.ModelMetrics,
                    diversityRate = c.DiversityRate
                }, JsonOptions)
            };
            db.CandidateSolutions.Add(entity);
            await db.SaveChangesAsync(ct);

            foreach (var (empId, days) in c.Assignments)
            {
                foreach (var (date, state) in days)
                {
                    db.Assignments.Add(new Assignment
                    {
                        OwnerType = AssignmentOwnerType.Candidate,
                        OwnerId = entity.Id,
                        EmployeeId = empId,
                        Date = date,
                        State = state.ToDisplay()
                    });
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
