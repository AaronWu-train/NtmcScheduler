using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;
using NtmScheduler.Core.SampleData;
using NtmScheduler.Core.Time;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.SampleData;

/// <summary>
/// Seeds a complete demo dataset so the UI can create Runs without CSV imports.
/// History is stored as IsCurrent <see cref="ScheduleSnapshot"/> rows (per month).
/// </summary>
public sealed class DemoDataSeeder : IDemoDataService
{
    private readonly NtmDbContext _db;
    private readonly AuditWriter _audit;

    public DemoDataSeeder(NtmDbContext db, AuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task SeedAsync(string? operatorName = null, CancellationToken ct = default)
    {
        var op = string.IsNullOrWhiteSpace(operatorName) ? "示範資料" : operatorName.Trim();
        var bundle = DemoDataset.Build();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await ClearOperationalAsync(ct);
        _db.ChangeTracker.Clear();
        await SeedEmployeesAsync(bundle, ct);
        await SeedMonthlyShiftsAsync(bundle, ct);
        await SeedCyclesAsync(bundle, ct);
        await SeedRulesAsync(ct);
        await SeedFixedEventsAsync(bundle, ct);
        await SeedHistoryAsync(bundle, op, ct);

        _audit.Add(op, "SeedDemoData", "DemoDataset", bundle.TargetMonth.ToString(), after: new
        {
            mCount = bundle.Employees.Count(e => e.Unit == Unit.M),
            tCount = bundle.Employees.Count(e => e.Unit == Unit.T),
            historyFrom = bundle.HistoryFrom,
            historyTo = bundle.HistoryTo,
            events = bundle.FixedEvents.Count
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task ClearOperationalAsync(CancellationToken ct)
    {
        await _db.ScheduleEdits.ExecuteDeleteAsync(ct);
        await _db.CandidateSolutions.ExecuteDeleteAsync(ct);
        await _db.ScheduleRuns.ExecuteDeleteAsync(ct);
        await _db.Assignments.ExecuteDeleteAsync(ct);
        await _db.MonthSchedules.ExecuteDeleteAsync(ct);
        await _db.ScheduleSnapshots.ExecuteDeleteAsync(ct);
        await _db.FixedEvents.ExecuteDeleteAsync(ct);
        await _db.EmployeeMonthlyShifts.ExecuteDeleteAsync(ct);
        await _db.Employees.ExecuteDeleteAsync(ct);
        await _db.ScheduleCycles.ExecuteDeleteAsync(ct);
        await _db.RuleSettings.ExecuteDeleteAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private async Task SeedEmployeesAsync(DemoBundle bundle, CancellationToken ct)
    {
        foreach (var e in bundle.Employees)
        {
            _db.Employees.Add(new Employee
            {
                Id = e.Id,
                Name = e.Name,
                Unit = e.Unit,
                HomeStation = e.HomeStation,
                Specialty = e.Specialty,
                Ability = e.Ability
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedMonthlyShiftsAsync(DemoBundle bundle, CancellationToken ct)
    {
        foreach (var ((empId, month), shift) in bundle.MonthlyShifts)
        {
            _db.EmployeeMonthlyShifts.Add(new EmployeeMonthlyShift
            {
                EmployeeId = empId,
                Month = month,
                Shift = shift
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedCyclesAsync(DemoBundle bundle, CancellationToken ct)
    {
        foreach (var c in bundle.Cycles)
        {
            _db.ScheduleCycles.Add(new ScheduleCycle
            {
                Start = c.Start,
                End = c.End,
                RequiredR = c.RequiredR,
                RequiredR1 = c.RequiredR1
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedRulesAsync(CancellationToken ct)
    {
        foreach (var unit in new[] { Unit.M, Unit.T })
        {
            foreach (var (ruleId, priority, order, enabled) in RuleCatalog.DefaultRows(unit))
            {
                _db.RuleSettings.Add(new RuleSetting
                {
                    Unit = unit,
                    RuleId = ruleId,
                    Priority = priority,
                    Enabled = enabled,
                    Order = order
                });
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedFixedEventsAsync(DemoBundle bundle, CancellationToken ct)
    {
        foreach (var e in bundle.FixedEvents)
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
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedHistoryAsync(DemoBundle bundle, string op, CancellationToken ct)
    {
        foreach (var ((unit, month), empDays) in bundle.HistoryByUnitMonth.OrderBy(x => x.Key.Month))
        {
            var snap = new ScheduleSnapshot
            {
                Unit = unit,
                Month = month,
                VersionNo = 1,
                CreatedAt = TaipeiTime.Now,
                Operator = op,
                IsCurrent = true
            };
            _db.ScheduleSnapshots.Add(snap);
            await _db.SaveChangesAsync(ct);

            foreach (var (empId, days) in empDays)
            {
                foreach (var (date, state) in days)
                {
                    _db.Assignments.Add(new Assignment
                    {
                        OwnerType = AssignmentOwnerType.Snapshot,
                        OwnerId = snap.Id,
                        EmployeeId = empId,
                        Date = date,
                        State = state.ToDisplay()
                    });
                }
            }

            await _db.SaveChangesAsync(ct);
        }
    }
}
