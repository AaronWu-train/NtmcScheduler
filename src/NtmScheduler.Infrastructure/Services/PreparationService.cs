using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Infrastructure.Data;

namespace NtmScheduler.Infrastructure.Services;

public sealed class PreparationService : IPreparationService
{
    private readonly NtmDbContext _db;

    public PreparationService(NtmDbContext db) => _db = db;

    public async Task<PreparationStatusDto> GetStatusAsync(
        Unit unit, YearMonth month, CancellationToken ct = default)
    {
        var items = new List<PreparationItemDto>();

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.Unit == unit).Select(e => e.Id).ToListAsync(ct);
        items.Add(new PreparationItemDto(
            "employees",
            "人員資料",
            employees.Count > 0,
            employees.Count > 0 ? Array.Empty<string>() : ["尚無人員"]));

        if (unit == Unit.T)
        {
            var shifts = await _db.EmployeeMonthlyShifts.AsNoTracking()
                .Where(s => s.Month == month.ToString())
                .Select(s => s.EmployeeId)
                .ToListAsync(ct);
            var missing = employees.Except(shifts).ToList();
            items.Add(new PreparationItemDto(
                "monthly_shifts",
                "T 月班組",
                missing.Count == 0 && employees.Count > 0,
                missing.Count == 0
                    ? Array.Empty<string>()
                    : missing.Select(id => $"缺少月班組：{id}").ToList()));
        }
        else
        {
            items.Add(new PreparationItemDto("monthly_shifts", "T 月班組", true, ["（M 單位不適用）"]));
        }

        var monthStart = month.FirstDay;
        var monthEnd = month.LastDay;
        var cycles = await _db.ScheduleCycles.AsNoTracking()
            .Where(c => c.Start <= monthEnd && c.End >= monthStart)
            .ToListAsync(ct);
        items.Add(new PreparationItemDto(
            "cycles",
            "週期涵蓋",
            cycles.Count > 0,
            cycles.Count > 0
                ? cycles.Select(c => $"{c.Start:yyyy-MM-dd}～{c.End:yyyy-MM-dd}（R={c.RequiredR}, R1={c.RequiredR1}）").ToList()
                : ["目標月無涵蓋的 8 週週期"]));

        var history = await _db.OfficialScheduleVersions.AsNoTracking()
            .AnyAsync(v => v.Unit == unit && v.IsCurrent, ct);
        items.Add(new PreparationItemDto(
            "history",
            "歷史涵蓋",
            history,
            history ? Array.Empty<string>() : ["尚無 Published 歷史（初次上線請先匯入）"]));

        var rules = await _db.RuleSettings.AsNoTracking().AnyAsync(r => r.Unit == unit, ct);
        items.Add(new PreparationItemDto(
            "settings",
            "固定設定／規則",
            true,
            rules ? Array.Empty<string>() : ["規則將於首次開啟規則頁時自動建立預設"]));

        return new PreparationStatusDto(unit, month, items);
    }
}
