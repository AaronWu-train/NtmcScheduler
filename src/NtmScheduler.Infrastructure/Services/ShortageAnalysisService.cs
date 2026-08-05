using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class ShortageAnalysisService : IShortageAnalysisService
{
    private readonly NtmDbContext _db;

    public ShortageAnalysisService(NtmDbContext db) => _db = db;

    public async Task<ShortageDto?> GetAsync(long runId, CancellationToken ct = default)
    {
        var candidate = await _db.CandidateSolutions.AsNoTracking()
            .FirstOrDefaultAsync(c => c.RunId == runId && c.IsShortageAnalysis, ct);
        if (candidate is null)
            return null;

        return new ShortageDto(
            runId,
            candidate.Id,
            null,
            Array.Empty<MCoverageRow>(),
            "缺班分析詳情於求解管線完成後提供");
    }
}
