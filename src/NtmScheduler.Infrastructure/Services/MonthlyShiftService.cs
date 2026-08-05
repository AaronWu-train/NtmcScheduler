using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Csv;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class MonthlyShiftService : IMonthlyShiftService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public MonthlyShiftService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyDictionary<string, ShiftType>> GetMonthAsync(
        YearMonth month, CancellationToken ct = default)
    {
        var rows = await _db.EmployeeMonthlyShifts.AsNoTracking()
            .Where(s => s.Month == month.ToString())
            .ToListAsync(ct);
        return rows.ToDictionary(s => s.EmployeeId, s => s.Shift);
    }

    public async Task UpsertAsync(
        string employeeId, YearMonth month, ShiftType shift, string op, CancellationToken ct = default)
    {
        var existing = await _db.EmployeeMonthlyShifts
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.Month == month.ToString(), ct);
        if (existing is null)
        {
            _db.EmployeeMonthlyShifts.Add(new EmployeeMonthlyShift
            {
                EmployeeId = employeeId,
                Month = month.ToString(),
                Shift = shift
            });
        }
        else
        {
            existing.Shift = shift;
        }

        _audit.Add(op, "UpsertMonthlyShift", "EmployeeMonthlyShift", $"{employeeId}/{month}",
            after: new { employeeId, month = month.ToString(), shift });
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string employeeId, YearMonth month, string op, CancellationToken ct = default)
    {
        var existing = await _db.EmployeeMonthlyShifts
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.Month == month.ToString(), ct);
        if (existing is null)
            return;
        _db.EmployeeMonthlyShifts.Remove(existing);
        _audit.Add(op, "DeleteMonthlyShift", "EmployeeMonthlyShift", $"{employeeId}/{month}");
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ImportResult> ImportCsvAsync(Stream csv, string op, CancellationToken ct = default)
    {
        try
        {
            var rows = MonthlyShiftCsv.Read(csv);
            var errors = new List<ImportError>();
            var ok = 0;
            var rowNum = 2;
            foreach (var row in rows)
            {
                try
                {
                    await UpsertAsync(row.EmployeeId, row.Month, row.Shift, op, ct);
                    ok++;
                }
                catch (Exception ex)
                {
                    errors.Add(new ImportError(rowNum, ex.Message, row.EmployeeId));
                }

                rowNum++;
            }

            return new ImportResult(ok, errors);
        }
        catch (Exception ex)
        {
            return ImportResult.Fail(new ImportError(null, $"CSV 解析失敗：{ex.Message}"));
        }
    }

    public async Task<byte[]> ExportCsvAsync(YearMonth? month = null, CancellationToken ct = default)
    {
        var q = _db.EmployeeMonthlyShifts.AsNoTracking().AsQueryable();
        if (month is { } m)
            q = q.Where(s => s.Month == m.ToString());
        var rows = await q.ToListAsync(ct);
        var data = rows.Select(r => new MonthlyShiftCsvRow(r.EmployeeId, YearMonth.Parse(r.Month), r.Shift));
        return MonthlyShiftCsv.Write(data);
    }
}
