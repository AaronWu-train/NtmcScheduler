using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Infrastructure.Data;

namespace NtmScheduler.Infrastructure.Services;

public sealed class AuditService : IAuditService
{
    private readonly NtmDbContext _db;

    public AuditService(NtmDbContext db) => _db = db;

    public async Task<IReadOnlyList<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Operator))
            q = q.Where(a => a.Operator.Contains(query.Operator));
        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action.Contains(query.Action));
        if (!string.IsNullOrWhiteSpace(query.TargetType))
            q = q.Where(a => a.TargetType == query.TargetType);
        if (!string.IsNullOrWhiteSpace(query.TargetId))
            q = q.Where(a => a.TargetId == query.TargetId);
        if (query.From is { } from)
            q = q.Where(a => a.At >= from);
        if (query.To is { } to)
            q = q.Where(a => a.At <= to);

        var take = Math.Clamp(query.Take, 1, 500);
        var rows = await q.OrderByDescending(a => a.At).Take(take).ToListAsync(ct);
        return rows.Select(a => new AuditLogDto(
            a.Id, a.At, a.Operator, a.Action, a.TargetType, a.TargetId, a.BeforeJson, a.AfterJson)).ToList();
    }
}
