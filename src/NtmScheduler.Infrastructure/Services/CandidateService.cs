using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Time;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class CandidateService : ICandidateService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public CandidateService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
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

    public async Task<long> PromoteToDraftAsync(long candidateId, string op, CancellationToken ct = default)
    {
        var candidate = await _db.CandidateSolutions
            .FirstOrDefaultAsync(c => c.Id == candidateId, ct)
            ?? throw new KeyNotFoundException($"找不到候選 {candidateId}");
        if (candidate.IsShortageAnalysis)
            throw new InvalidOperationException("缺班分析不可複製為 Draft");

        var draft = new DraftSchedule
        {
            RunId = candidate.RunId,
            SourceCandidateId = candidate.Id,
            CreatedAt = TaipeiTime.Now,
            Operator = op
        };
        _db.DraftSchedules.Add(draft);
        await _db.SaveChangesAsync(ct);

        var source = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == AssignmentOwnerType.Candidate && a.OwnerId == candidateId)
            .ToListAsync(ct);
        foreach (var a in source)
        {
            _db.Assignments.Add(new Assignment
            {
                OwnerType = AssignmentOwnerType.Draft,
                OwnerId = draft.Id,
                EmployeeId = a.EmployeeId,
                Date = a.Date,
                State = a.State
            });
        }

        _audit.Add(op, "Candidate.PromoteToDraft", "DraftSchedule", draft.Id.ToString(),
            after: new { candidateId, draft.Id });
        await _db.SaveChangesAsync(ct);
        return draft.Id;
    }

    private static CandidateDto Map(CandidateSolution c)
    {
        IReadOnlyDictionary<string, int> metrics = new Dictionary<string, int>();
        try
        {
            if (!string.IsNullOrWhiteSpace(c.MetricsJson) && c.MetricsJson != "{}")
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(c.MetricsJson);
                if (dict is not null)
                    metrics = dict;
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return new CandidateDto(c.Id, c.RunId, c.Index, c.IsShortageAnalysis, metrics, null, c.MetricsJson);
    }
}
