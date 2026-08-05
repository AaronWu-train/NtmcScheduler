using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Time;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Csv;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

/// <summary>
/// Imports historical schedule.csv (+ optional events.csv) as a current ScheduleSnapshot (D-05).
/// </summary>
public sealed class HistoryImportService : IHistoryImportService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public HistoryImportService(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<ImportResult> ImportAsync(
        Stream scheduleCsv, Stream? eventsCsv, string op, CancellationToken ct = default)
    {
        ScheduleCsvDocument doc;
        try
        {
            doc = ScheduleCsv.Read(scheduleCsv);
        }
        catch (Exception ex)
        {
            return ImportResult.Fail(new ImportError(null, $"schedule.csv 解析失敗：{ex.Message}"));
        }

        var hasXInSchedule = doc.Rows.Any(r => r.DayStates.Values.Any(s => s == "X"));
        if (hasXInSchedule && eventsCsv is null)
        {
            return ImportResult.Fail(new ImportError(null,
                "歷史含 X 但缺 events.csv（INVALID_INPUT）"));
        }

        var month = ScheduleCsv.InferTargetMonth(doc.Dates);
        var errors = new List<ImportError>();
        var ok = 0;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        foreach (var row in doc.Rows)
        {
            var emp = await _db.Employees.FindAsync([row.EmployeeId], ct);
            if (emp is null)
            {
                emp = new Employee
                {
                    Id = row.EmployeeId,
                    Name = row.Name,
                    Unit = doc.Unit,
                    HomeStation = doc.Unit == Unit.M ? row.ThirdColumnValue : null,
                    Specialty = null
                };
                _db.Employees.Add(emp);
            }

            ok++;
        }

        await _db.SaveChangesAsync(ct);

        var current = await _db.ScheduleSnapshots
            .Where(v => v.Unit == doc.Unit && v.Month == month.ToString() && v.IsCurrent)
            .ToListAsync(ct);
        foreach (var v in current)
            v.IsCurrent = false;

        var nextNo = await _db.ScheduleSnapshots
            .Where(v => v.Unit == doc.Unit && v.Month == month.ToString())
            .Select(v => (int?)v.VersionNo)
            .MaxAsync(ct) ?? 0;

        var snap = new ScheduleSnapshot
        {
            Unit = doc.Unit,
            Month = month.ToString(),
            VersionNo = nextNo + 1,
            CreatedAt = TaipeiTime.Now,
            Operator = op,
            IsCurrent = true
        };
        _db.ScheduleSnapshots.Add(snap);
        await _db.SaveChangesAsync(ct);

        foreach (var row in doc.Rows)
        {
            foreach (var (date, state) in row.DayStates)
            {
                if (string.IsNullOrWhiteSpace(state))
                    continue;
                _db.Assignments.Add(new Assignment
                {
                    OwnerType = AssignmentOwnerType.Snapshot,
                    OwnerId = snap.Id,
                    EmployeeId = row.EmployeeId,
                    Date = date,
                    State = state // preserve R1
                });
            }
        }

        if (eventsCsv is not null)
        {
            try
            {
                var events = EventsCsv.Read(eventsCsv);
                foreach (var e in events)
                {
                    _db.FixedEvents.Add(new FixedEvent
                    {
                        EmployeeId = e.EmployeeId,
                        Type = e.Type,
                        Date = e.Date,
                        Start = e.Start,
                        End = e.End,
                        Description = e.Description
                    });
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ImportError(null, $"events.csv 解析失敗：{ex.Message}"));
            }
        }

        _audit.Add(op, "HistoryImport", "ScheduleSnapshot", snap.Id.ToString(),
            after: new { doc.Unit, month = month.ToString(), rows = doc.Rows.Count });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ImportResult(ok, errors);
    }
}
