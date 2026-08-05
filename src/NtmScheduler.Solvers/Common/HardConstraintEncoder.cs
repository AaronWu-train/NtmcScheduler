using Google.OrTools.Sat;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation;

namespace NtmScheduler.Solvers.Common;

public static class HardConstraintEncoder
{
    /// <summary>
    /// GEN-H-02: cw IntVar channeling. rest→0, r1→carry, work→+1, domain ≤6.
    /// </summary>
    public static void EncodeContinuousWork(
        CpModel model,
        SolveRequestView req,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        foreach (var emp in req.Employees)
        {
            var seed = ContinuousWorkCounter.RebuildUpTo(CreateSeedContext(req), emp.Id,
                req.Period.FirstDay.AddDays(-1));
            IntVar prev = model.NewConstant(Math.Clamp(seed, 0, 6));

            foreach (var d in req.Period.AllDays)
            {
                if (!days.TryGetValue((emp.Id, d), out var dv)) continue;
                var cw = model.NewIntVar(0, 6, $"cw_{emp.Id}_{d:yyyyMMdd}");

                if (dv.IsFixed)
                {
                    switch (dv.FixedState!.Value.Type)
                    {
                        case DayStateType.Rest:
                        case DayStateType.RStar:
                            model.Add(cw == 0);
                            break;
                        case DayStateType.HolidayRest:
                            model.Add(cw == prev);
                            break;
                        case DayStateType.Shift:
                        case DayStateType.X:
                            model.Add(cw == prev + 1);
                            break;
                        default:
                            model.Add(cw == prev);
                            break;
                    }
                }
                else
                {
                    model.Add(cw == 0).OnlyEnforceIf(dv.Rest!);
                    model.Add(cw == prev).OnlyEnforceIf(dv.R1!);
                    model.Add(cw == prev + 1).OnlyEnforceIf(dv.WorkAny!);
                }

                prev = cw;
            }
        }
    }

    /// <summary>GEN-H-04 four separate constraint groups (never merge rest+r1).</summary>
    public static void EncodeCycleRest(
        CpModel model,
        SolveRequestView req,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var cycles = CycleResolver.Intersecting(req.Cycles, req.Period.FirstDay, req.Period.RangeEnd);

        foreach (var emp in req.Employees)
        {
            foreach (var cycle in cycles)
            {
                var restTerms = new List<LinearExpr>();
                var r1Terms = new List<LinearExpr>();
                var restToMonth = new List<LinearExpr>();

                for (var d = cycle.Start; d <= cycle.End && d <= req.Period.RangeEnd; d = d.AddDays(1))
                {
                    if (days.TryGetValue((emp.Id, d), out var dv) && !dv.IsFixed)
                    {
                        restTerms.Add(dv.Rest!);
                        r1Terms.Add(dv.R1!);
                        if (d <= req.Period.MonthEnd)
                            restToMonth.Add(dv.Rest!);
                        continue;
                    }

                    var state = req.GetFixed(emp.Id, d);
                    if (state is null) continue;
                    if (state.Value.IsGeneralRest)
                    {
                        restTerms.Add(model.NewConstant(1));
                        if (d <= req.Period.MonthEnd)
                            restToMonth.Add(model.NewConstant(1));
                    }

                    if (state.Value.Type == DayStateType.HolidayRest)
                        r1Terms.Add(model.NewConstant(1));
                }

                LinearExpr SumOr0(List<LinearExpr> t) =>
                    t.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(t);

                var sumRest = SumOr0(restTerms);
                var sumR1 = SumOr0(r1Terms);

                if (cycle.End <= req.Period.RangeEnd)
                {
                    model.Add(sumRest == cycle.RequiredR);
                    model.Add(sumR1 == cycle.RequiredR1);
                }
                else
                {
                    model.Add(sumRest <= cycle.RequiredR);
                    model.Add(sumR1 <= cycle.RequiredR1);
                    var remaining = cycle.End.DayNumber - req.Period.RangeEnd.DayNumber;
                    model.Add((cycle.RequiredR - sumRest) + (cycle.RequiredR1 - sumR1) <= remaining);
                }

                if (cycle.End > req.Period.MonthEnd)
                {
                    var reserved = cycle.ReservedGeneralRest(req.Period.MonthEnd);
                    model.Add(SumOr0(restToMonth) <= cycle.RequiredR - reserved);
                }
            }
        }
    }

    /// <summary>GEN-H-03 via precomputed shift-OR literals on each EmployeeDayVars.</summary>
    public static void EncodeRestGap(
        CpModel model,
        SolveRequestView req,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var rangeStart = req.Period.FirstDay;
        var rangeEnd = req.Period.RangeEnd;
        var forbidden = RestGapAnalyzer.ForbiddenNormalPairs(req.Unit, rangeStart, rangeEnd);

        foreach (var emp in req.Employees)
        {
            foreach (var pair in forbidden)
            {
                var a = GetShiftOr(days, emp.Id, pair.First.Date, pair.First.Shift, model);
                var b = GetShiftOr(days, emp.Id, pair.Second.Date, pair.Second.Shift, model);
                if (a is null || b is null) continue;
                model.AddBoolOr(new ILiteral[] { a.Not(), b.Not() });
            }

            // LastWorkEnd vs first variable/fixed works
            if (req.Histories.TryGetValue(emp.Id, out var hist) && hist.LastWorkEnd is { } lastEnd)
            {
                foreach (var d in req.Period.AllDays)
                {
                    foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
                    {
                        var (start, _) = ShiftTimeConfig.Interval(req.Unit, d, shift);
                        if (!RestGapAnalyzer.Violates(lastEnd, start)) continue;
                        var lit = GetShiftOr(days, emp.Id, d, shift, model);
                        if (lit is not null)
                            model.Add(lit == 0);
                    }
                    // stop scanning once we pass days that could still be first work? keep all — LastWorkEnd is absolute
                }
            }

            foreach (var x in req.XEvents.Where(e => e.EmployeeId == emp.Id))
            {
                for (var delta = -2; delta <= 2; delta++)
                {
                    var d = x.StartDate.AddDays(delta);
                    if (d < rangeStart || d > rangeEnd) continue;

                    foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
                    {
                        if (d == x.StartDate) continue;
                        var (start, end) = ShiftTimeConfig.Interval(req.Unit, d, shift);
                        var overlaps = start < x.End && end > x.Start;
                        var beforeBad = end <= x.Start && RestGapAnalyzer.Violates(end, x.Start);
                        var afterBad = start >= x.End && RestGapAnalyzer.Violates(x.End, start);
                        if (!overlaps && !beforeBad && !afterBad) continue;

                        var lit = GetShiftOr(days, emp.Id, d, shift, model);
                        if (lit is not null)
                            model.Add(lit == 0);
                    }
                }

                // Fixed X day itself is already assigned; ensure history LastWorkEnd gap
                if (req.Histories.TryGetValue(emp.Id, out var hx) && hx.LastWorkEnd is { } le
                    && RestGapAnalyzer.Violates(le, x.Start)
                    && x.StartDate >= rangeStart && x.StartDate <= rangeEnd)
                {
                    model.Add(model.NewConstant(1) == 0);
                }
            }
        }
    }

    private static BoolVar? GetShiftOr(
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        string empId,
        DateOnly date,
        ShiftType shift,
        CpModel model)
    {
        if (!days.TryGetValue((empId, date), out var dv))
            return null;

        if (dv.IsFixed)
        {
            if (dv.FixedState!.Value.IsNormalShift && dv.FixedState.Value.Shift == shift)
            {
                var t = model.NewBoolVar($"fix_shift_{empId}_{date:yyyyMMdd}_{shift}");
                model.Add(t == 1);
                return t;
            }

            return null; // fixed to something else ⇒ pair auto-satisfied
        }

        if (dv.ShiftOr.TryGetValue(shift, out var existing))
            return existing;

        var lits = dv.Work.Where(kv => kv.Key.Shift == shift).Select(kv => kv.Value).ToList();
        if (lits.Count == 0) return null;
        if (lits.Count == 1)
        {
            dv.ShiftOr[shift] = lits[0];
            return lits[0];
        }

        var or = PatternEncoder.Or(model, $"shiftOr_{empId}_{date:yyyyMMdd}_{shift}", lits);
        dv.ShiftOr[shift] = or;
        return or;
    }

    private static ScheduleContext CreateSeedContext(SolveRequestView req) => new()
    {
        Period = req.Period,
        Unit = req.Unit,
        Employees = req.Employees,
        Cycles = req.Cycles,
        Histories = req.Histories,
        XEvents = req.XEvents,
        Assignments = new Dictionary<string, IReadOnlyDictionary<DateOnly, DayState>>(),
        MonthlyShifts = req.Request.MonthlyShifts,
        NextMonthShifts = req.Request.NextMonthShifts,
        RStarRequests = req.RStarRequests,
    };
}
