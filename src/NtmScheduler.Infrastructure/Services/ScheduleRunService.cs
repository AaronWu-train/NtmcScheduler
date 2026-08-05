using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Time;
using NtmScheduler.Core.Validation;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class ScheduleRunService : IScheduleRunService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public ScheduleRunService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<CreateRunResult> CreateAsync(
        Unit unit, YearMonth month, string op, CancellationToken ct = default)
    {
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.Unit == unit)
            .CountAsync(ct);
        if (employees == 0)
        {
            return CreateRunResult.Invalid(new ValidationError(
                "E01_NO_EMPLOYEES",
                $"單位 {unit} 尚無人員資料"));
        }

        var snapshot = new
        {
            unit = unit.ToString(),
            targetMonth = month.ToString(),
            seed = 42,
            programVersion = typeof(ScheduleRunService).Assembly.GetName().Version?.ToString()
        };

        var run = new ScheduleRun
        {
            Unit = unit,
            TargetMonth = month.ToString(),
            Status = ScheduleRunStatus.Queued,
            Seed = 42,
            ProgramVersion = snapshot.programVersion ?? "unknown",
            Operator = op,
            CreatedAt = TaipeiTime.Now,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions)
        };

        _db.ScheduleRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        _audit.Add(op, "CreateRun", "ScheduleRun", run.Id.ToString(), after: snapshot);
        await _db.SaveChangesAsync(ct);

        return CreateRunResult.Ok(run.Id);
    }

    public async Task<RunProgressDto> GetProgressAsync(long runId, CancellationToken ct = default)
    {
        var run = await _db.ScheduleRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new KeyNotFoundException($"找不到 ScheduleRun {runId}");

        string? currentRule = null;
        IReadOnlyList<string> completed = Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(run.ProgressJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(run.ProgressJson);
                if (doc.RootElement.TryGetProperty("currentRuleId", out var cur) && cur.ValueKind == JsonValueKind.String)
                    currentRule = cur.GetString();
                if (doc.RootElement.TryGetProperty("completedRuleIds", out var done) && done.ValueKind == JsonValueKind.Array)
                    completed = done.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
            }
            catch (JsonException)
            {
                // ignore malformed progress
            }
        }

        string? error = run.Status == ScheduleRunStatus.Failed
            ? TryReadMessage(run.ProgressJson)
            : null;

        return new RunProgressDto(
            run.Id,
            run.Unit,
            MapLifecycle(run.Status),
            MapScheduleStatus(run.ScheduleStatus),
            MapOptimizationStatus(run.OptimizationStatus),
            currentRule,
            completed,
            run.CandidateCount,
            run.ShortageAnalysisAvailable,
            error);
    }

    private static string? TryReadMessage(string? progressJson)
    {
        if (string.IsNullOrWhiteSpace(progressJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(progressJson);
            if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return msg.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public async Task<IReadOnlyList<RunSummaryDto>> ListAsync(Unit? unit = null, CancellationToken ct = default)
    {
        var query = _db.ScheduleRuns.AsNoTracking().AsQueryable();
        if (unit is { } u)
            query = query.Where(r => r.Unit == u);

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return rows.Select(r => new RunSummaryDto(
            r.Id,
            r.Unit,
            YearMonth.Parse(r.TargetMonth),
            MapLifecycle(r.Status),
            MapScheduleStatus(r.ScheduleStatus),
            r.CreatedAt,
            r.Operator,
            MapOptimizationStatus(r.OptimizationStatus),
            r.CandidateCount)).ToList();
    }

    private static RunLifecycleStatus MapLifecycle(ScheduleRunStatus status) => status switch
    {
        ScheduleRunStatus.Queued => RunLifecycleStatus.Queued,
        ScheduleRunStatus.Running => RunLifecycleStatus.Running,
        ScheduleRunStatus.Completed => RunLifecycleStatus.Completed,
        ScheduleRunStatus.Failed => RunLifecycleStatus.Failed,
        _ => RunLifecycleStatus.Failed
    };

    private static ScheduleStatus? MapScheduleStatus(ScheduleStatusCode? status) => status switch
    {
        ScheduleStatusCode.Feasible => ScheduleStatus.Feasible,
        ScheduleStatusCode.Infeasible => ScheduleStatus.Infeasible,
        ScheduleStatusCode.InvalidInput => ScheduleStatus.InvalidInput,
        null => null,
        _ => null
    };

    private static OptimizationStatus? MapOptimizationStatus(OptimizationStatusCode? status) => status switch
    {
        OptimizationStatusCode.Optimal => OptimizationStatus.Optimal,
        OptimizationStatusCode.TimeLimit => OptimizationStatus.TimeLimit,
        null => null,
        _ => null
    };
}
