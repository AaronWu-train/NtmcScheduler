using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class RuleSettingService : IRuleSettingService
{
    private static readonly (string RuleId, int Priority, bool Soft)[] DefaultM =
    [
        ("GEN-H-01", 0, false), ("GEN-H-02", 0, false), ("GEN-H-03", 0, false),
        ("GEN-H-04", 0, false), ("GEN-H-05", 0, false),
        ("M-H-01", 0, false), ("M-H-02", 0, false), ("M-H-03", 0, false),
        ("GEN-R-01", 1, true),
        ("M-S-EXT", 2, true), ("M-S-HOME", 2, true),
        ("GEN-S-STREAK", 3, true), ("M-S-BLOCK", 3, true),
        ("M-S-NIGHT-EARLY", 3, true), ("M-S-NIGHT-AFTERNOON", 3, true),
        ("M-S-RESTSWITCH", 3, true), ("M-S-ROTATE", 3, true),
        ("GEN-S-WEEKDAY-R", 4, true), ("GEN-S-WEEKEND-R", 4, true), ("M-S-SUPPORT-FAIR", 4, true)
    ];

    private static readonly (string RuleId, int Priority, bool Soft)[] DefaultT =
    [
        ("GEN-H-01", 0, false), ("GEN-H-02", 0, false), ("GEN-H-03", 0, false),
        ("GEN-H-04", 0, false), ("GEN-H-05", 0, false),
        ("T-H-01", 0, false),
        ("GEN-R-01", 1, true),
        ("T-S-ATTEND", 2, true), ("T-S-SPECIALTY", 2, true), ("T-S-ABILITY", 2, true),
        ("T-S-MONTH-REST", 3, true), ("T-S-MONTH-BALANCE", 3, true),
        ("GEN-S-WEEKDAY-R", 4, true), ("GEN-S-WEEKEND-R", 4, true)
    ];

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

        var defs = unit == Unit.M ? DefaultM : DefaultT;
        var order = 0;
        foreach (var (ruleId, priority, _) in defs)
        {
            _db.RuleSettings.Add(new RuleSetting
            {
                Unit = unit,
                RuleId = ruleId,
                Priority = priority,
                Enabled = true,
                Order = order++,
                ParametersJson = null
            });
        }

        await _db.SaveChangesAsync(ct);
    }
}
