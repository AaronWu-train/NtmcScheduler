using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class CandidateService : ICandidateService
{
    private readonly NtmDbContext _db;

    public CandidateService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _ = audit;
    }

    public async Task<IReadOnlyList<CandidateDto>> GetAsync(long runId, CancellationToken ct = default)
    {
        var rows = await _db.CandidateSolutions.AsNoTracking()
            .Where(c => c.RunId == runId && !c.IsShortageAnalysis)
            .OrderBy(c => c.Index)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<CandidateCompareDto> CompareAsync(long runId, CancellationToken ct = default)
    {
        var candidates = await GetAsync(runId, ct);
        var ruleIds = candidates
            .SelectMany(c => c.RuleMetrics.Keys)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        return new CandidateCompareDto(ruleIds, candidates);
    }

    private static CandidateDto Map(CandidateSolution c)
    {
        IReadOnlyDictionary<string, int> metrics = new Dictionary<string, int>();
        double? diversityRate = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(c.MetricsJson) && c.MetricsJson != "{}")
            {
                using var doc = JsonDocument.Parse(c.MetricsJson);
                if (doc.RootElement.TryGetProperty("violations", out var violations))
                    metrics = violations.Deserialize<Dictionary<string, int>>() ?? metrics;
                if (doc.RootElement.TryGetProperty("diversityRate", out var diversity)
                    && diversity.ValueKind == JsonValueKind.Number)
                    diversityRate = diversity.GetDouble();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return new CandidateDto(c.Id, c.RunId, c.Index, c.IsShortageAnalysis, metrics, diversityRate, c.MetricsJson);
    }
}
