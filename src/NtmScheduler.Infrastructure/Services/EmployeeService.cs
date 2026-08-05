using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Csv;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class EmployeeService : IEmployeeService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public EmployeeService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<EmployeeInfo>> ListAsync(Unit unit, CancellationToken ct = default)
    {
        var rows = await _db.Employees.AsNoTracking()
            .Where(e => e.Unit == unit)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
        return rows.Select(e => new EmployeeInfo(e.Id, e.Name, e.Unit, e.HomeStation, e.Specialty, e.Ability)).ToList();
    }

    public async Task UpsertAsync(EmployeeInfo employee, string op, CancellationToken ct = default)
    {
        if (employee.Unit == Unit.T && employee.Ability is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(employee), "T 單位能力值必須為 1–5");

        var existing = await _db.Employees.FindAsync([employee.Id], ct);
        if (existing is null)
        {
            _db.Employees.Add(new Employee
            {
                Id = employee.Id,
                Name = employee.Name,
                Unit = employee.Unit,
                HomeStation = employee.HomeStation,
                Specialty = employee.Specialty,
                Ability = employee.Ability
            });
            _audit.Add(op, "CreateEmployee", "Employee", employee.Id, after: employee);
        }
        else
        {
            var before = new EmployeeInfo(existing.Id, existing.Name, existing.Unit, existing.HomeStation, existing.Specialty, existing.Ability);
            existing.Name = employee.Name;
            existing.Unit = employee.Unit;
            existing.HomeStation = employee.HomeStation;
            existing.Specialty = employee.Specialty;
            existing.Ability = employee.Ability;
            _audit.Add(op, "UpdateEmployee", "Employee", employee.Id, before, employee);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, string op, CancellationToken ct = default)
    {
        var existing = await _db.Employees.FindAsync([id], ct);
        if (existing is null)
            return;
        _db.Employees.Remove(existing);
        _audit.Add(op, "DeleteEmployee", "Employee", id, before: existing);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ImportResult> ImportCsvAsync(Unit unit, Stream csv, string op, CancellationToken ct = default)
    {
        try
        {
            var parsed = EmployeeCsv.Read(unit, csv);
            var errors = new List<ImportError>();
            var ok = 0;
            var row = 2;
            foreach (var emp in parsed)
            {
                try
                {
                    await UpsertAsync(emp, op, ct);
                    ok++;
                }
                catch (Exception ex)
                {
                    errors.Add(new ImportError(row, ex.Message, emp.Id));
                }

                row++;
            }

            return new ImportResult(ok, errors);
        }
        catch (Exception ex)
        {
            return ImportResult.Fail(new ImportError(null, $"CSV 解析失敗：{ex.Message}"));
        }
    }

    public async Task<byte[]> ExportCsvAsync(Unit unit, CancellationToken ct = default)
    {
        var list = await ListAsync(unit, ct);
        return EmployeeCsv.Write(unit, list);
    }
}
