using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Domain;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class ScheduleCycleService : IScheduleCycleService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public ScheduleCycleService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<CycleInfo>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.ScheduleCycles.AsNoTracking()
            .OrderBy(c => c.Start)
            .ToListAsync(ct);
        return rows.Select(c => new CycleInfo(c.Start, c.End, c.RequiredR, c.RequiredR1)).ToList();
    }

    public async Task UpsertAsync(CycleInfo cycle, string op, CancellationToken ct = default)
    {
        if (cycle.End < cycle.Start)
            throw new ArgumentException("週期結束日不得早於開始日");
        if (cycle.RequiredR < 0 || cycle.RequiredR1 < 0)
            throw new ArgumentException("requiredR / requiredR1 不得為負");

        var existing = await _db.ScheduleCycles.FirstOrDefaultAsync(c => c.Start == cycle.Start, ct);
        if (existing is null)
        {
            _db.ScheduleCycles.Add(new ScheduleCycle
            {
                Start = cycle.Start,
                End = cycle.End,
                RequiredR = cycle.RequiredR,
                RequiredR1 = cycle.RequiredR1
            });
            _audit.Add(op, "CreateCycle", "ScheduleCycle", cycle.Start.ToString("yyyy-MM-dd"), after: cycle);
        }
        else
        {
            var before = new CycleInfo(existing.Start, existing.End, existing.RequiredR, existing.RequiredR1);
            existing.End = cycle.End;
            existing.RequiredR = cycle.RequiredR;
            existing.RequiredR1 = cycle.RequiredR1;
            _audit.Add(op, "UpdateCycle", "ScheduleCycle", cycle.Start.ToString("yyyy-MM-dd"), before, cycle);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(DateOnly start, string op, CancellationToken ct = default)
    {
        var existing = await _db.ScheduleCycles.FirstOrDefaultAsync(c => c.Start == start, ct);
        if (existing is null)
            return;
        _db.ScheduleCycles.Remove(existing);
        _audit.Add(op, "DeleteCycle", "ScheduleCycle", start.ToString("yyyy-MM-dd"));
        await _db.SaveChangesAsync(ct);
    }
}
