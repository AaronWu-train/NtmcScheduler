using Google.OrTools.Sat;
using NtmScheduler.Core.Domain;
using NtmScheduler.Solvers.Common;

namespace NtmScheduler.Solvers;

public static class SolutionExtractor
{
    public static (
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> Assignments,
        IReadOnlySet<(string Station, DateOnly Date, ShiftType Shift)> External)
        ExtractM(BuiltModel built, CpSolver solver)
    {
        var map = new Dictionary<string, Dictionary<DateOnly, DayState>>();
        foreach (var emp in built.Request.Employees)
            map[emp.Id] = new Dictionary<DateOnly, DayState>();

        foreach (var ((empId, date), dv) in built.Days)
        {
            DayState state;
            if (dv.IsFixed)
            {
                state = dv.FixedState!.Value;
            }
            else if (solver.Value(dv.Rest!) == 1)
            {
                state = built.Request.IsRStarRequest(empId, date) ? DayState.RStar : DayState.Rest;
            }
            else if (solver.Value(dv.R1!) == 1)
            {
                state = DayState.HolidayRest;
            }
            else
            {
                var hit = dv.Work.First(kv => solver.Value(kv.Value) == 1);
                state = DayState.Work(hit.Key.Shift, hit.Key.Station);
            }

            map[empId][date] = state;
        }

        var external = new HashSet<(string, DateOnly, ShiftType)>();
        foreach (var (key, lit) in built.Ext)
        {
            if (solver.Value(lit) == 1)
                external.Add(key);
        }

        // Shortage: mark UNASSIGNED where slack=1
        foreach (var (key, slack) in built.Slack)
        {
            if (solver.Value(slack) != 1) continue;
            // No employee assignment for this slot — leave as-is; coverage reports unassigned.
            _ = key;
        }

        return (
            map.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<DateOnly, DayState>)kv.Value),
            external);
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, DayState>> ExtractT(
        BuiltModel built, CpSolver solver)
    {
        var map = new Dictionary<string, Dictionary<DateOnly, DayState>>();
        foreach (var emp in built.Request.Employees)
            map[emp.Id] = new Dictionary<DateOnly, DayState>();

        foreach (var ((empId, date), dv) in built.Days)
        {
            DayState state;
            if (dv.IsFixed)
            {
                state = dv.FixedState!.Value;
            }
            else if (solver.Value(dv.Rest!) == 1)
            {
                state = built.Request.IsRStarRequest(empId, date) ? DayState.RStar : DayState.Rest;
            }
            else if (solver.Value(dv.R1!) == 1)
            {
                state = DayState.HolidayRest;
            }
            else
            {
                var shift = built.Request.ResolveTShift(empId, date);
                state = DayState.Work(shift);
            }

            map[empId][date] = state;
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<DateOnly, DayState>)kv.Value);
    }

    public static IReadOnlyDictionary<string, int> ReadMetrics(BuiltModel built, CpSolver solver)
    {
        var dict = new Dictionary<string, int>();
        foreach (var (ruleId, obj) in built.SoftObjectives)
        {
            if (ruleId == "SHORTAGE") continue;
            dict[ruleId] = (int)solver.Value(obj);
        }
        return dict;
    }

}
