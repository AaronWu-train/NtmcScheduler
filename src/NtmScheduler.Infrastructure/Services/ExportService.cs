using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Infrastructure.Csv;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Data.Entities;

namespace NtmScheduler.Infrastructure.Services;

public sealed class ExportService : IExportService
{
    private readonly NtmDbContext _db;

    public ExportService(NtmDbContext db) => _db = db;

    public async Task<byte[]> ScheduleCsvAsync(OwnerRef solution, CancellationToken ct = default)
    {
        var assignments = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == solution.OwnerType && a.OwnerId == solution.OwnerId)
            .ToListAsync(ct);

        if (assignments.Count == 0)
            throw new InvalidOperationException($"找不到班表指派：{solution.OwnerType}/{solution.OwnerId}");

        var empIds = assignments.Select(a => a.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var unit = employees.Values.Select(e => e.Unit).Distinct().Single();
        var dates = assignments.Select(a => a.Date).Distinct().OrderBy(d => d).ToList();
        var month = ScheduleCsv.InferTargetMonth(dates);

        Dictionary<string, ShiftType>? monthShifts = null;
        if (unit == Unit.T)
        {
            var key = month.ToString();
            monthShifts = await _db.EmployeeMonthlyShifts.AsNoTracking()
                .Where(s => s.Month == key && empIds.Contains(s.EmployeeId))
                .ToDictionaryAsync(s => s.EmployeeId, s => s.Shift, ct);
        }

        var rows = new List<ScheduleCsvRow>();
        foreach (var g in assignments.GroupBy(a => a.EmployeeId).OrderBy(x => x.Key))
        {
            if (!employees.TryGetValue(g.Key, out var emp))
                continue;

            var dayStates = g.ToDictionary(a => a.Date, a => a.State);
            var monthR = dayStates.Count(kv =>
                kv.Key.Year == month.Year && kv.Key.Month == month.Month && kv.Value is "R" or "R*");
            var monthR1 = dayStates.Count(kv =>
                kv.Key.Year == month.Year && kv.Key.Month == month.Month && kv.Value == "R1");
            var cycleR = dayStates.Count(kv => kv.Value is "R" or "R*");
            var cycleR1 = dayStates.Count(kv => kv.Value == "R1");

            string? third = unit == Unit.M
                ? emp.HomeStation
                : monthShifts is not null && monthShifts.TryGetValue(emp.Id, out var sh)
                    ? sh.ToDisplay()
                    : null;

            var full = new Dictionary<DateOnly, string>();
            foreach (var d in dates)
                full[d] = dayStates.TryGetValue(d, out var s) ? s : "";

            rows.Add(new ScheduleCsvRow(emp.Id, emp.Name, third, full, monthR, monthR1, cycleR, cycleR1));
        }

        var doc = new ScheduleCsvDocument(
            unit,
            unit == Unit.M ? ScheduleCsv.HomeStationHeader : ScheduleCsv.ShiftHeader,
            dates,
            rows);
        return ScheduleCsv.Write(doc);
    }

    public async Task<byte[]> CoverageCsvAsync(OwnerRef solution, CancellationToken ct = default)
    {
        if (solution.OwnerType == AssignmentOwnerType.Candidate)
        {
            var candidate = await _db.CandidateSolutions.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == solution.OwnerId, ct)
                ?? throw new KeyNotFoundException($"找不到候選：{solution.OwnerId}");
            if (!string.IsNullOrWhiteSpace(candidate.CoverageCsv))
                return WithUtf8Bom(candidate.CoverageCsv);
        }

        var unit = await InferUnitAsync(solution, ct);
        return unit == Unit.M
            ? CoverageCsv.WriteM(Array.Empty<MCoverageCsvRow>())
            : CoverageCsv.WriteT(Array.Empty<TCoverageCsvRow>());
    }

    public async Task<byte[]> ViolationsCsvAsync(OwnerRef solution, CancellationToken ct = default)
    {
        if (solution.OwnerType == AssignmentOwnerType.Candidate)
        {
            var candidate = await _db.CandidateSolutions.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == solution.OwnerId, ct)
                ?? throw new KeyNotFoundException($"找不到候選：{solution.OwnerId}");
            if (!string.IsNullOrWhiteSpace(candidate.ViolationsCsv))
                return WithUtf8Bom(candidate.ViolationsCsv);
        }

        return ViolationsCsv.Write(Array.Empty<ViolationCsvRow>());
    }

    public async Task<string> ResultJsonAsync(long runId, CancellationToken ct = default)
    {
        var run = await _db.ScheduleRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new KeyNotFoundException($"找不到 ScheduleRun：{runId}");

        if (!string.IsNullOrWhiteSpace(run.ResultJson))
            return run.ResultJson;

        var payload = new
        {
            scheduleStatus = run.ScheduleStatus switch
            {
                ScheduleStatusCode.Feasible => "FEASIBLE",
                ScheduleStatusCode.Infeasible => "INFEASIBLE",
                ScheduleStatusCode.InvalidInput => "INVALID_INPUT",
                _ => null
            },
            optimizationStatus = run.OptimizationStatus switch
            {
                OptimizationStatusCode.Optimal => "OPTIMAL",
                OptimizationStatusCode.TimeLimit => "TIME_LIMIT",
                _ => null
            },
            candidateCount = run.CandidateCount,
            shortageAnalysisAvailable = run.ShortageAnalysisAvailable
        };
        return JsonSerializer.Serialize(payload);
    }

    private async Task<Unit> InferUnitAsync(OwnerRef solution, CancellationToken ct)
    {
        var empId = await _db.Assignments.AsNoTracking()
            .Where(a => a.OwnerType == solution.OwnerType && a.OwnerId == solution.OwnerId)
            .Select(a => a.EmployeeId)
            .FirstOrDefaultAsync(ct);

        if (empId is not null)
        {
            var emp = await _db.Employees.AsNoTracking().FirstAsync(e => e.Id == empId, ct);
            return emp.Unit;
        }

        if (solution.OwnerType == AssignmentOwnerType.Candidate)
        {
            return await _db.CandidateSolutions.AsNoTracking()
                .Where(c => c.Id == solution.OwnerId)
                .Select(c => c.Run!.Unit)
                .FirstOrDefaultAsync(ct);
        }

        return Unit.M;
    }

    private static byte[] WithUtf8Bom(string content)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, CsvWriter.Utf8Bom, leaveOpen: true))
            writer.Write(content);
        return ms.ToArray();
    }
}
