using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation;

public sealed record MCoverageRow(
    DateOnly Date,
    string Location,
    ShiftType Shift,
    int Required,
    int Assigned,
    int External,
    int Unassigned);

public sealed record TCoverageRow(
    DateOnly Date,
    ShiftType Shift,
    int GroupSize,
    int NormalAttend,
    int AttendTarget,
    double AvgAbility,
    IReadOnlyList<string> MissingSpecialties);

public static class CoverageCalculator
{
    public static IReadOnlyList<MCoverageRow> ComputeM(ScheduleContext ctx)
    {
        var rows = new List<MCoverageRow>();
        foreach (var d in ctx.Period.AllDays)
        {
            foreach (var station in StationConfig.AllStations)
            {
                foreach (var shift in StationConfig.ShiftsForStation(station))
                {
                    var assigned = 0;
                    foreach (var emp in ctx.Employees)
                    {
                        var state = ctx.GetState(emp.Id, d);
                        if (state?.IsNormalShift != true) continue;
                        if (state.Value.Shift != shift) continue;
                        var st = state.Value.Station ?? emp.HomeStation;
                        if (st == station) assigned++;
                    }

                    var external = ctx.ExternalSlots.Contains((station, d, shift)) ? 1 : 0;
                    const int required = 1;
                    var unassigned = Math.Max(0, required - assigned - external);
                    rows.Add(new MCoverageRow(d, station, shift, required, assigned, external, unassigned));
                }
            }
        }

        return rows;
    }

    public static IReadOnlyList<TCoverageRow> ComputeT(ScheduleContext ctx)
    {
        var rows = new List<TCoverageRow>();
        if (ctx.MonthlyShifts is null) return rows;

        foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
        {
            var members = ctx.Employees
                .Where(e => ctx.MonthlyShifts.TryGetValue(e.Id, out var s) && s == shift)
                .ToList();
            var groupSize = members.Count;
            var target = groupSize / 2; // floor

            foreach (var d in ctx.Period.TargetMonthDays)
            {
                var attending = members.Where(e =>
                {
                    var st = ctx.GetState(e.Id, d);
                    return st?.IsNormalShift == true;
                }).ToList();

                var avg = attending.Count == 0
                    ? 0d
                    : attending.Average(e => e.Ability ?? 0);

                var specialties = members
                    .Select(m => m.Specialty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .Cast<string>()
                    .ToList();

                var missing = specialties.Where(sp =>
                    !attending.Any(a => a.Specialty == sp)).ToList();

                rows.Add(new TCoverageRow(d, shift, groupSize, attending.Count, target, avg, missing));
            }
        }

        return rows;
    }
}
