using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class RuleSettingService : IRuleSettingService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public RuleSettingService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RuleSettingDto>> GetAsync(Unit unit, CancellationToken ct = default)
    {
        await EnsureDefaultsAsync(unit, ct);
        var rows = await _db.RuleSettings.AsNoTracking()
            .Where(r => r.Unit == unit)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Order)
            .ToListAsync(ct);
        return rows.Select(r => new RuleSettingDto(
            r.Id, r.Unit, r.RuleId, r.Priority, r.Enabled, r.Order, r.ParametersJson)).ToList();
    }

    public async Task UpdateAsync(
        Unit unit, IReadOnlyList<RuleSettingDto> ordered, string op, CancellationToken ct = default)
    {
        var existing = await _db.RuleSettings.Where(r => r.Unit == unit).ToListAsync(ct);
        var byId = existing.ToDictionary(r => r.Id);

        foreach (var dto in ordered)
        {
            if (!byId.TryGetValue(dto.Id, out var row))
                continue;

            // P0 locked; P1 fixed highest among soft — cannot disable or reorder away
            if (row.Priority == 0)
                continue;

            if (row.Priority == 1)
            {
                row.ParametersJson = dto.ParametersJson;
                row.Enabled = true;
                continue;
            }

            row.Enabled = dto.Enabled;
            row.Order = dto.Order;
            row.ParametersJson = dto.ParametersJson;
            // allow priority change only within P2–P4
            if (dto.Priority is >= 2 and <= 4)
                row.Priority = dto.Priority;
        }

        _audit.Add(op, "UpdateRules", "RuleSetting", unit.ToString(), after: ordered);
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureDefaultsAsync(Unit unit, CancellationToken ct)
    {
        if (await _db.RuleSettings.AnyAsync(r => r.Unit == unit, ct))
            return;

        foreach (var (ruleId, priority, order, enabled) in RuleCatalog.DefaultRows(unit))
        {
            _db.RuleSettings.Add(new RuleSetting
            {
                Unit = unit,
                RuleId = ruleId,
                Priority = priority,
                Enabled = enabled,
                Order = order,
                ParametersJson = null
            });
        }

        await _db.SaveChangesAsync(ct);
    }
}
