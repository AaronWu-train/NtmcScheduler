using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Validation;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Csv;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class EventService : IEventService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public EventService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FixedEventDto>> ListAsync(
        Unit unit, YearMonth month, CancellationToken ct = default)
    {
        var empIds = await _db.Employees.AsNoTracking()
            .Where(e => e.Unit == unit)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var start = month.FirstDay;
        var end = month.LastDay;

        var rows = await _db.FixedEvents.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId))
            .Where(e =>
                (e.Type == FixedEventType.RStar && e.Date >= start && e.Date <= end) ||
                (e.Type == FixedEventType.X && e.Start != null &&
                 DateOnly.FromDateTime(e.Start.Value) <= end &&
                 DateOnly.FromDateTime(e.End ?? e.Start.Value) >= start))
            .OrderBy(e => e.EmployeeId)
            .ThenBy(e => e.Date)
            .ThenBy(e => e.Start)
            .ToListAsync(ct);

        return rows.Select(Map).ToList();
    }

    public async Task<ValidationError[]> AddRStarAsync(
        string employeeId, DateOnly date, string op, CancellationToken ct = default)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId, ct))
            return [new ValidationError("E02_UNKNOWN_EMPLOYEE", $"未知員工：{employeeId}", employeeId, date)];

        var dup = await _db.FixedEvents.AnyAsync(e =>
            e.EmployeeId == employeeId && e.Type == FixedEventType.RStar && e.Date == date, ct);
        if (dup)
            return [new ValidationError("E03_DUP_RSTAR", $"重複的 R*：{employeeId} @ {date}", employeeId, date)];

        _db.FixedEvents.Add(new FixedEvent
        {
            EmployeeId = employeeId,
            Type = FixedEventType.RStar,
            Date = date
        });
        _audit.Add(op, "AddRStar", "FixedEvent", $"{employeeId}/{date}", after: new { employeeId, date });
        await _db.SaveChangesAsync(ct);
        return [];
    }

    public async Task<ValidationError[]> AddXAsync(XEvent x, string op, CancellationToken ct = default)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == x.EmployeeId, ct))
            return [new ValidationError("E02_UNKNOWN_EMPLOYEE", $"未知員工：{x.EmployeeId}", x.EmployeeId)];

        if (x.End <= x.Start)
            return [new ValidationError("E04_X_TIME_ORDER", "X 結束時間必須晚於開始時間", x.EmployeeId)];

        _db.FixedEvents.Add(new FixedEvent
        {
            EmployeeId = x.EmployeeId,
            Type = FixedEventType.X,
            Start = x.Start,
            End = x.End,
            Description = x.Description
        });
        _audit.Add(op, "AddX", "FixedEvent", x.EmployeeId, after: x);
        await _db.SaveChangesAsync(ct);
        return [];
    }

    public async Task DeleteAsync(long eventId, string op, CancellationToken ct = default)
    {
        var existing = await _db.FixedEvents.FindAsync([eventId], ct);
        if (existing is null)
            return;
        _db.FixedEvents.Remove(existing);
        _audit.Add(op, "DeleteEvent", "FixedEvent", eventId.ToString(), before: existing);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ImportResult> ImportCsvAsync(Stream csv, string op, CancellationToken ct = default)
    {
        try
        {
            var rows = EventsCsv.Read(csv);
            var errors = new List<ImportError>();
            var ok = 0;
            var rowNum = 2;
            foreach (var row in rows)
            {
                try
                {
                    if (row.Type == FixedEventType.RStar)
                    {
                        var errs = await AddRStarAsync(row.EmployeeId, row.Date!.Value, op, ct);
                        if (errs.Length > 0)
                            errors.Add(new ImportError(rowNum, errs[0].Message, row.EmployeeId));
                        else
                            ok++;
                    }
                    else
                    {
                        var errs = await AddXAsync(
                            new XEvent(row.EmployeeId, row.Start!.Value, row.End!.Value, row.Description ?? ""),
                            op, ct);
                        if (errs.Length > 0)
                            errors.Add(new ImportError(rowNum, errs[0].Message, row.EmployeeId));
                        else
                            ok++;
                    }
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

    public async Task<byte[]> ExportCsvAsync(
        Unit? unit = null, YearMonth? month = null, CancellationToken ct = default)
    {
        var q = _db.FixedEvents.AsNoTracking().AsQueryable();
        if (unit is { } u)
        {
            var ids = await _db.Employees.AsNoTracking()
                .Where(e => e.Unit == u).Select(e => e.Id).ToListAsync(ct);
            q = q.Where(e => ids.Contains(e.EmployeeId));
        }

        if (month is { } m)
        {
            var start = m.FirstDay;
            var end = m.LastDay;
            q = q.Where(e =>
                (e.Type == FixedEventType.RStar && e.Date >= start && e.Date <= end) ||
                (e.Type == FixedEventType.X && e.Start != null &&
                 DateOnly.FromDateTime(e.Start.Value) <= end &&
                 DateOnly.FromDateTime(e.End ?? e.Start.Value) >= start));
        }

        var rows = await q.ToListAsync(ct);
        var csvRows = rows.Select(e => new EventsCsvRow(
            e.EmployeeId, e.Type, e.Date, e.Start, e.End, e.Description));
        return EventsCsv.Write(csvRows);
    }

    private static FixedEventDto Map(FixedEvent e) =>
        new(e.Id, e.EmployeeId, e.Type, e.Date, e.Start, e.End, e.Description);
}
