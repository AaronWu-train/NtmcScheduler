using Google.OrTools.Sat;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;
using NtmScheduler.Solvers.Common;

namespace NtmScheduler.Solvers.M;

public sealed class MModelBuilder
{
    private readonly SolveRequestView _req;
    private readonly bool _allowShortage;

    public MModelBuilder(SolveRequest request, bool allowShortage = false)
    {
        _req = new SolveRequestView { Request = request };
        _allowShortage = allowShortage;
    }

    public BuiltModel Build()
    {
        var model = new CpModel();
        var days = new Dictionary<(string Emp, DateOnly Date), EmployeeDayVars>();
        var built = new BuiltModel { Model = model, Days = days, Request = _req };

        CreateVariables(model, days, built);
        HardConstraintEncoder.EncodeContinuousWork(model, _req, days);
        HardConstraintEncoder.EncodeRestGap(model, _req, days);
        HardConstraintEncoder.EncodeCycleRest(model, _req, days);
        EncodeCoverage(model, days, built);
        EncodeSoftObjectives(model, days, built);
        return built;
    }

    private void CreateVariables(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        BuiltModel built)
    {
        foreach (var emp in _req.Employees)
        {
            var home = emp.HomeStation ?? throw new InvalidOperationException($"M 員工缺少本站：{emp.Id}");
            var groupStations = StationConfig.StationsInGroup(StationConfig.GroupOf(home)).ToList();

            foreach (var d in _req.Period.AllDays)
            {
                var fixedState = GetPeriodFixed(emp.Id, d);
                if (fixedState is { } fs)
                {
                    days[(emp.Id, d)] = new EmployeeDayVars
                    {
                        EmployeeId = emp.Id,
                        Date = d,
                        IsFixed = true,
                        FixedState = fs
                    };
                    continue;
                }

                var rest = model.NewBoolVar($"rest_{emp.Id}_{d:yyyyMMdd}");
                var r1 = model.NewBoolVar($"r1_{emp.Id}_{d:yyyyMMdd}");
                var dv = new EmployeeDayVars
                {
                    EmployeeId = emp.Id,
                    Date = d,
                    Rest = rest,
                    R1 = r1
                };

                var workLits = new List<BoolVar>();
                foreach (var st in groupStations)
                {
                    foreach (var shift in StationConfig.ShiftsForStation(st))
                    {
                        var x = model.NewBoolVar($"x_{emp.Id}_{d:yyyyMMdd}_{st}_{shift}");
                        dv.Work[(st, shift)] = x;
                        workLits.Add(x);
                    }
                }

                var workAny = workLits.Count == 1
                    ? workLits[0]
                    : PatternEncoder.Or(model, $"work_{emp.Id}_{d:yyyyMMdd}", workLits);
                dv.WorkAny = workAny;

                // exactly-one: Σx + rest + r1 == 1
                var sum = new List<BoolVar>(workLits) { rest, r1 };
                model.Add(LinearExpr.Sum(sum) == 1);

                // Precompute shift OR
                foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
                {
                    var lits = dv.Work.Where(kv => kv.Key.Shift == shift).Select(kv => kv.Value).ToList();
                    if (lits.Count > 0)
                        dv.ShiftOr[shift] = lits.Count == 1
                            ? lits[0]
                            : PatternEncoder.Or(model, $"sor_{emp.Id}_{d:yyyyMMdd}_{shift}", lits);
                }

                days[(emp.Id, d)] = dv;
            }
        }

        // ext vars only for external stations that are active in this request
        foreach (var st in ActiveStations().Where(StationConfig.ExternalStations.Contains))
        {
            foreach (var d in _req.Period.AllDays)
            {
                foreach (var shift in StationConfig.ShiftsForStation(st))
                {
                    built.Ext[(st, d, shift)] = model.NewBoolVar($"ext_{st}_{d:yyyyMMdd}_{shift}");
                }
            }
        }
    }

    /// <summary>
    /// Stations that require coverage in this request = distinct home stations of employees.
    /// Full production (48 staff) covers all 12; small fixtures may scope to a subset.
    /// Staff may still work at other same-group stations (M-H-01 variables).
    /// </summary>
    private IReadOnlyList<string> ActiveStations() =>
        _req.Employees
            .Select(e => e.HomeStation)
            .Where(s => s is not null)
            .Cast<string>()
            .Distinct()
            .OrderBy(s => s)
            .ToList();

    private void EncodeCoverage(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        BuiltModel built)
    {
        foreach (var d in _req.Period.AllDays)
        {
            foreach (var st in ActiveStations())
            {
                foreach (var shift in StationConfig.ShiftsForStation(st))
                {
                    var terms = new List<LinearExpr>();
                    var fixedCount = 0;
                    foreach (var emp in _req.Employees)
                    {
                        if (!days.TryGetValue((emp.Id, d), out var dv)) continue;
                        if (dv.IsFixed)
                        {
                            if (dv.FixedState!.Value.IsNormalShift
                                && dv.FixedState.Value.Shift == shift
                                && (dv.FixedState.Value.Station ?? emp.HomeStation) == st)
                                fixedCount++;
                            continue;
                        }

                        if (dv.Work.TryGetValue((st, shift), out var lit))
                            terms.Add(lit);
                    }

                    if (fixedCount > 0)
                        terms.Add(model.NewConstant(fixedCount));

                    if (built.Ext.TryGetValue((st, d, shift), out var ext))
                        terms.Add(ext);

                    if (_allowShortage)
                    {
                        var slack = model.NewBoolVar($"slack_{st}_{d:yyyyMMdd}_{shift}");
                        built.Slack[(st, d, shift)] = slack;
                        terms.Add(slack);
                    }

                    if (terms.Count == 0)
                    {
                        if (!_allowShortage)
                            model.Add(model.NewConstant(1) == 0);
                        continue;
                    }

                    model.Add(LinearExpr.Sum(terms) == 1);
                }
            }
        }
    }

    private void EncodeSoftObjectives(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        BuiltModel built)
    {
        foreach (var rule in _req.Request.SoftRules.Where(r => r.Enabled).OrderBy(r => r.Order))
        {
            built.SoftObjectives[rule.RuleId] = rule.RuleId switch
            {
                "GEN-R-01" => EncodeGenR01(model, days),
                "M-S-EXT" => EncodeMsExt(model, built),
                "M-S-HOME" => EncodeMsHome(model, days),
                "GEN-S-STREAK" => EncodeGenSStreak(model, days),
                "M-S-BLOCK" => EncodeMsBlock(model, days),
                "M-S-NIGHT-EARLY" => EncodeNightPattern(model, days, ShiftType.Morning, "M-S-NIGHT-EARLY"),
                "M-S-NIGHT-AFTERNOON" => EncodeNightPattern(model, days, ShiftType.Afternoon, "M-S-NIGHT-AFTERNOON"),
                "M-S-RESTSWITCH" => EncodeRestSwitch(model, days),
                "M-S-ROTATE" => EncodeRotate(model, days),
                "GEN-S-WEEKDAY-R" => EncodeFairnessWeekday(model, days),
                "GEN-S-WEEKEND-R" => EncodeFairnessWeekend(model, days),
                "M-S-SUPPORT-FAIR" => EncodeSupportFair(model, days),
                _ => model.NewConstant(0)
            };
        }

        if (_allowShortage)
        {
            var slackSum = built.Slack.Values.Cast<LinearExpr>().ToList();
            var obj = model.NewIntVar(0, Math.Max(1, slackSum.Count), "obj_shortage");
            model.Add(obj == (slackSum.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(slackSum)));
            built.SoftObjectives["SHORTAGE"] = obj;
        }
    }

    private IntVar EncodeGenR01(CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var terms = new List<LinearExpr>();
        foreach (var (empId, date) in _req.RStarRequests)
        {
            if (!_req.Period.IsInTargetMonth(date)) continue;
            if (!days.TryGetValue((empId, date), out var dv) || dv.IsFixed)
            {
                if (dv?.FixedState?.Type != DayStateType.RStar)
                    terms.Add(model.NewConstant(1));
                continue;
            }

            // unsatisfied = 1 - rest
            terms.Add(PatternEncoder.OneMinus(dv.Rest!));
        }

        var obj = model.NewIntVar(0, Math.Max(1, terms.Count), "obj_GEN-R-01");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private IntVar EncodeMsExt(CpModel model, BuiltModel built)
    {
        var terms = built.Ext
            .Where(kv => _req.Period.IsInTargetMonth(kv.Key.Date))
            .Select(kv => (LinearExpr)kv.Value)
            .ToList();
        var obj = model.NewIntVar(0, Math.Max(1, terms.Count), "obj_M-S-EXT");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private IntVar EncodeMsHome(CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var terms = new List<LinearExpr>();
        foreach (var emp in _req.Employees)
        {
            foreach (var d in _req.Period.TargetMonthDays)
            {
                if (!days.TryGetValue((emp.Id, d), out var dv) || dv.IsFixed)
                {
                    if (dv?.FixedState?.IsNormalShift == true)
                    {
                        var st = dv.FixedState.Value.Station ?? emp.HomeStation;
                        if (st != emp.HomeStation) terms.Add(model.NewConstant(1));
                    }
                    continue;
                }

                foreach (var (key, lit) in dv.Work)
                {
                    if (key.Station != emp.HomeStation)
                        terms.Add(lit);
                }
            }
        }

        var obj = model.NewIntVar(0, Math.Max(1, terms.Count), "obj_M-S-HOME");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private IntVar EncodeGenSStreak(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        // Pattern windows for lengths 1,2,6 ending on/before MonthEnd; plus tail excess.
        var allScores = new List<IntVar>();
        foreach (var emp in _req.Employees)
        {
            var score = EncodeStreakForEmployee(model, emp.Id, days);
            allScores.Add(score);
        }

        var obj = model.NewIntVar(0, 10_000, "obj_GEN-S-STREAK");
        model.Add(obj == LinearExpr.Sum(allScores));
        return obj;
    }

    private IntVar EncodeStreakForEmployee(
        CpModel model,
        string empId,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        // isWork / isBoundary for history+period up to MonthEnd
        var monthEnd = _req.Period.MonthEnd;
        var histStart = _req.Histories.TryGetValue(empId, out var hist) && hist.Days.Count > 0
            ? hist.Days.Keys.Min()
            : _req.Period.FirstDay;

        BoolVar IsWork(DateOnly d)
        {
            if (days.TryGetValue((empId, d), out var dv))
            {
                if (dv.IsFixed)
                {
                    var t = model.NewBoolVar($"sw_{empId}_{d:yyyyMMdd}");
                    model.Add(t == (dv.FixedState!.Value.IsWorkDay ? 1 : 0));
                    return t;
                }
                return dv.WorkAny!;
            }

            var state = _req.GetFixed(empId, d);
            var c = model.NewBoolVar($"sw_h_{empId}_{d:yyyyMMdd}");
            model.Add(c == (state?.IsWorkDay == true ? 1 : 0));
            return c;
        }

        BoolVar IsBoundary(DateOnly d)
        {
            // boundary = any rest (R/R*/R1) — ends segment
            if (days.TryGetValue((empId, d), out var dv))
            {
                if (dv.IsFixed)
                {
                    var t = model.NewBoolVar($"sb_{empId}_{d:yyyyMMdd}");
                    model.Add(t == (dv.FixedState!.Value.IsAnyRest ? 1 : 0));
                    return t;
                }
                return PatternEncoder.Or(model, $"sb_{empId}_{d:yyyyMMdd}", [dv.Rest!, dv.R1!]);
            }

            var state = _req.GetFixed(empId, d);
            var c = model.NewBoolVar($"sb_h_{empId}_{d:yyyyMMdd}");
            model.Add(c == (state?.IsAnyRest == true ? 1 : 0));
            return c;
        }

        var scores = new List<LinearExpr>();
        // Detect closed segments of length L∈{1,2,6} ending at e where e in target month.
        // Pattern: boundary (or start) then L works then boundary, ending day of work in [FirstDay, MonthEnd]
        for (var end = _req.Period.FirstDay; end <= monthEnd; end = end.AddDays(1))
        {
            foreach (var L in new[] { 1, 2, 6 })
            {
                var start = end.AddDays(1 - L);
                if (start < histStart) continue;
                var lits = new List<BoolVar>();
                // before start: boundary or start==histStart with no prior work — use boundary at start-1 if exists
                var before = start.AddDays(-1);
                if (before >= histStart)
                    lits.Add(IsBoundary(before));
                // else treat as boundary (segment starts at history beginning)

                for (var i = 0; i < L; i++)
                    lits.Add(IsWork(start.AddDays(i)));

                var after = end.AddDays(1);
                if (after <= monthEnd || after <= _req.Period.RangeEnd)
                {
                    // need boundary after — if after > MonthEnd, segment is unfinished for D-13 (don't score shortness)
                    if (after > monthEnd)
                        continue;
                    lits.Add(IsBoundary(after));
                }
                else continue;

                var ind = PatternEncoder.And(model, $"streak_{empId}_{end:yyyyMMdd}_{L}", lits);
                var w = L switch { 1 => 2, 2 => 1, 6 => 1, _ => 0 };
                if (w > 0)
                {
                    var term = model.NewIntVar(0, w, $"streak_t_{empId}_{end:yyyyMMdd}_{L}");
                    model.Add(term == w).OnlyEnforceIf(ind);
                    model.Add(term == 0).OnlyEnforceIf(ind.Not());
                    scores.Add(term);
                }
            }
        }

        // Tail excess: working on MonthEnd → max(0, length_to_month_end - 5)
        // length = consecutive work ending at MonthEnd
        var tailScores = new List<LinearExpr>();
        for (var L = 6; L <= 20; L++)
        {
            var start = monthEnd.AddDays(1 - L);
            if (start < histStart) continue;
            var lits = new List<BoolVar>();
            var before = start.AddDays(-1);
            if (before >= histStart)
                lits.Add(IsBoundary(before));
            for (var i = 0; i < L; i++)
                lits.Add(IsWork(start.AddDays(i)));
            var ind = PatternEncoder.And(model, $"tail_{empId}_{L}", lits);
            var excess = L - 5;
            var term = model.NewIntVar(0, excess, $"tail_t_{empId}_{L}");
            model.Add(term == excess).OnlyEnforceIf(ind);
            model.Add(term == 0).OnlyEnforceIf(ind.Not());
            // Only the exact length should fire — longer patterns also contain shorter; use exact via boundary before.
            // Multiple L can be true only for one exact length if boundary before is required.
            tailScores.Add(term);
        }

        scores.AddRange(tailScores);
        var obj = model.NewIntVar(0, 500, $"obj_streak_{empId}");
        model.Add(obj == (scores.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(scores)));
        return obj;
    }

    private IntVar EncodeMsBlock(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var scores = new List<IntVar>();
        foreach (var emp in _req.Employees)
        {
            var dayList = _req.Period.AllDays.ToList();
            _req.Histories.TryGetValue(emp.Id, out var hist);
            BlockCounterEncoder.Encode(
                model,
                emp.Id,
                dayList,
                _req.Period.MonthEnd,
                d => days.TryGetValue((emp.Id, d), out var dv) && !dv.IsFixed && dv.ShiftOr.TryGetValue(ShiftType.Morning, out var m) ? m : FixedShiftLit(model, emp.Id, d, days, ShiftType.Morning),
                d => days.TryGetValue((emp.Id, d), out var dv) && !dv.IsFixed && dv.ShiftOr.TryGetValue(ShiftType.Afternoon, out var a) ? a : FixedShiftLit(model, emp.Id, d, days, ShiftType.Afternoon),
                d => days.TryGetValue((emp.Id, d), out var dv) && !dv.IsFixed && dv.ShiftOr.TryGetValue(ShiftType.Night, out var n) ? n : FixedShiftLit(model, emp.Id, d, days, ShiftType.Night),
                _ => false,
                hist?.OpenBlock,
                out var empObj);
            scores.Add(empObj);
        }

        var obj = model.NewIntVar(0, 10_000, "obj_M-S-BLOCK");
        model.Add(obj == LinearExpr.Sum(scores));
        return obj;
    }

    private static BoolVar? FixedShiftLit(
        CpModel model, string empId, DateOnly d,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        ShiftType shift)
    {
        if (!days.TryGetValue((empId, d), out var dv) || !dv.IsFixed) return null;
        if (dv.FixedState!.Value.IsNormalShift && dv.FixedState.Value.Shift == shift)
        {
            var t = model.NewBoolVar($"fix_s_{empId}_{d:yyyyMMdd}_{shift}");
            model.Add(t == 1);
            return t;
        }

        if (dv.IsFixed)
        {
            var f = model.NewBoolVar($"fix_n_{empId}_{d:yyyyMMdd}_{shift}");
            model.Add(f == 0);
            return f;
        }

        return null;
    }

    private IntVar EncodeNightPattern(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        ShiftType third,
        string ruleId)
    {
        var terms = new List<LinearExpr>();
        foreach (var emp in _req.Employees)
        {
            for (var mid = _req.Period.FirstDay; mid <= _req.Period.MonthEnd; mid = mid.AddDays(1))
            {
                var prev = mid.AddDays(-1);
                var next = mid.AddDays(1);
                if (_req.Period.IsExtensionDay(next)) continue;

                var night = GetShiftBool(model, days, emp.Id, prev, ShiftType.Night);
                var rest = GetGeneralRestBool(model, days, emp.Id, mid);
                var thirdLit = GetShiftBool(model, days, emp.Id, next, third);
                if (night is null || rest is null || thirdLit is null) continue;

                var ind = PatternEncoder.And(model, $"{ruleId}_{emp.Id}_{mid:yyyyMMdd}",
                    [night, rest, thirdLit]);
                terms.Add(ind);
            }
        }

        var obj = model.NewIntVar(0, Math.Max(1, terms.Count), $"obj_{ruleId}");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private IntVar EncodeRestSwitch(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days) =>
        EncodeAdjacentShiftPenalty(model, days, "M-S-RESTSWITCH", rotateOnly: false);

    private IntVar EncodeRotate(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days) =>
        EncodeAdjacentShiftPenalty(model, days, "M-S-ROTATE", rotateOnly: true);

    private IntVar EncodeAdjacentShiftPenalty(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        string ruleId,
        bool rotateOnly)
    {
        var terms = new List<LinearExpr>();
        var shifts = new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night };

        foreach (var emp in _req.Employees)
        {
            var histStart = _req.Histories.TryGetValue(emp.Id, out var hist) && hist.Days.Count > 0
                ? hist.Days.Keys.Min()
                : _req.Period.FirstDay;

            // Enumerate pairs (d1 < d2) with d2 in target month
            for (var d2 = _req.Period.FirstDay; d2 <= _req.Period.MonthEnd; d2 = d2.AddDays(1))
            {
                for (var d1 = histStart; d1 < d2; d1 = d1.AddDays(1))
                {
                    // noNormalBetween: every mid day is rest/r1/X (not normal shift)
                    // hasRestBetween: some mid is general rest
                    var mids = new List<DateOnly>();
                    for (var m = d1.AddDays(1); m < d2; m = m.AddDays(1))
                        mids.Add(m);

                    // Skip if any mid cannot be determined — still encode
                    foreach (var s1 in shifts)
                    foreach (var s2 in shifts)
                    {
                        if (s1 == s2) continue;
                        if (rotateOnly && IsPreferredRotate(s1, s2)) continue;

                        var lit1 = GetShiftBool(model, days, emp.Id, d1, s1);
                        var lit2 = GetShiftBool(model, days, emp.Id, d2, s2);
                        if (lit1 is null || lit2 is null) continue;

                        var andLits = new List<BoolVar> { lit1, lit2 };
                        foreach (var m in mids)
                        {
                            var notNormal = GetNotNormalBool(model, days, emp.Id, m);
                            if (notNormal is null) { andLits = null!; break; }
                            andLits.Add(notNormal);
                        }

                        if (andLits is null) continue;

                        if (!rotateOnly)
                        {
                            // RESTSWITCH: need NOT hasRestBetween
                            if (mids.Count > 0)
                            {
                                var restMids = mids
                                    .Select(m => GetGeneralRestBool(model, days, emp.Id, m))
                                    .Where(x => x is not null)
                                    .Cast<BoolVar>()
                                    .ToList();
                                if (restMids.Count > 0)
                                {
                                    var hasRest = PatternEncoder.Or(model,
                                        $"hr_{emp.Id}_{d1:yyyyMMdd}_{d2:yyyyMMdd}", restMids);
                                    // AND with ¬hasRest
                                    var noRest = model.NewBoolVar($"nr_{emp.Id}_{d1:yyyyMMdd}_{d2:yyyyMMdd}");
                                    model.Add(noRest == 1).OnlyEnforceIf(hasRest.Not());
                                    model.Add(noRest == 0).OnlyEnforceIf(hasRest);
                                    // simpler: use hasRest.Not() in And — but Not() returns ILiteral
                                    // Create bool equal to not hasRest
                                    andLits.Add(noRest);
                                    model.AddImplication(noRest, hasRest.Not());
                                    model.AddImplication(hasRest.Not(), noRest);
                                }
                            }
                        }

                        var ind = PatternEncoder.And(model,
                            $"{ruleId}_{emp.Id}_{d1:yyyyMMdd}_{d2:yyyyMMdd}_{s1}_{s2}", andLits);
                        terms.Add(ind);
                    }
                }
            }
        }

        var obj = model.NewIntVar(0, Math.Max(1, terms.Count), $"obj_{ruleId}");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private static bool IsPreferredRotate(ShiftType a, ShiftType b) =>
        (a == ShiftType.Morning && b == ShiftType.Afternoon)
        || (a == ShiftType.Afternoon && b == ShiftType.Night)
        || (a == ShiftType.Night && b == ShiftType.Morning);

    private IntVar EncodeFairnessWeekday(
        CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var groups = _req.Employees
            .GroupBy(e => StationConfig.GroupOf(e.HomeStation!))
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(e => e.Id).ToList()))
            .ToList();
        return FairnessEncoder.EncodeRestSpread(
            model, "GEN-S-WEEKDAY-R", _req.Cycles, _req.Period.MonthEnd, _req.Period.FirstDay, groups,
            (emp, d) => days.TryGetValue((emp, d), out var dv) && !dv.IsFixed ? dv.Rest : null,
            (emp, d) =>
            {
                var s = _req.GetFixed(emp, d);
                if (days.TryGetValue((emp, d), out var dv) && dv.IsFixed)
                    s = dv.FixedState;
                return s?.IsGeneralRest;
            });
    }

    private IntVar EncodeFairnessWeekend(
        CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var groups = _req.Employees
            .GroupBy(e => StationConfig.GroupOf(e.HomeStation!))
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(e => e.Id).ToList()))
            .ToList();
        return FairnessEncoder.EncodeWeekendRestSpread(
            model, "GEN-S-WEEKEND-R", _req.Cycles, _req.Period.MonthEnd, _req.Period.FirstDay, groups,
            (emp, d) => days.TryGetValue((emp, d), out var dv) && !dv.IsFixed ? dv.Rest : null,
            (emp, d) =>
            {
                var s = _req.GetFixed(emp, d);
                if (days.TryGetValue((emp, d), out var dv) && dv.IsFixed)
                    s = dv.FixedState;
                return s?.IsGeneralRest;
            });
    }

    private IntVar EncodeSupportFair(
        CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var groups = _req.Employees
            .GroupBy(e => StationConfig.GroupOf(e.HomeStation!))
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(e => e.Id).ToList()))
            .ToList();

        // Augment with history constants inside FairnessEncoder by folding into nonHomeWork
        return FairnessEncoder.EncodeSupportFair(
            model, _req.Cycles, _req.Period.MonthEnd, _req.Period.FirstDay, groups,
            (empId, d) =>
            {
                var emp = _req.Employees.First(e => e.Id == empId);
                if (days.TryGetValue((empId, d), out var dv) && !dv.IsFixed)
                {
                    var nonHome = dv.Work.Where(kv => kv.Key.Station != emp.HomeStation)
                        .Select(kv => kv.Value).ToList();
                    if (nonHome.Count == 0) return null;
                    return nonHome.Count == 1
                        ? nonHome[0]
                        : PatternEncoder.Or(model, $"nh_{empId}_{d:yyyyMMdd}", nonHome);
                }

                var s = _req.GetFixed(empId, d) ?? (dv?.FixedState);
                if (s?.IsNormalShift == true)
                {
                    var st = s.Value.Station ?? emp.HomeStation;
                    if (st != emp.HomeStation)
                    {
                        var t = model.NewBoolVar($"nhf_{empId}_{d:yyyyMMdd}");
                        model.Add(t == 1);
                        return t;
                    }
                }

                return null;
            });
    }

    private BoolVar? GetShiftBool(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        string empId, DateOnly d, ShiftType shift)
    {
        if (days.TryGetValue((empId, d), out var dv))
        {
            if (!dv.IsFixed)
                return dv.ShiftOr.TryGetValue(shift, out var lit) ? lit : null;
            if (dv.FixedState!.Value.IsNormalShift && dv.FixedState.Value.Shift == shift)
            {
                var t = model.NewBoolVar($"gs_{empId}_{d:yyyyMMdd}_{shift}");
                model.Add(t == 1);
                return t;
            }

            return null;
        }

        var state = _req.GetFixed(empId, d);
        if (state?.IsNormalShift == true && state.Value.Shift == shift)
        {
            var t = model.NewBoolVar($"gsh_{empId}_{d:yyyyMMdd}_{shift}");
            model.Add(t == 1);
            return t;
        }

        return null;
    }

    private BoolVar? GetGeneralRestBool(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        string empId, DateOnly d)
    {
        if (days.TryGetValue((empId, d), out var dv))
        {
            if (!dv.IsFixed) return dv.Rest;
            var t = model.NewBoolVar($"gr_{empId}_{d:yyyyMMdd}");
            model.Add(t == (dv.FixedState!.Value.IsGeneralRest ? 1 : 0));
            return t;
        }

        var state = _req.GetFixed(empId, d);
        if (state is null) return null;
        var c = model.NewBoolVar($"grh_{empId}_{d:yyyyMMdd}");
        model.Add(c == (state.Value.IsGeneralRest ? 1 : 0));
        return c;
    }

    private BoolVar? GetNotNormalBool(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days,
        string empId, DateOnly d)
    {
        if (days.TryGetValue((empId, d), out var dv))
        {
            if (!dv.IsFixed)
            {
                // not normal = rest OR r1 (X would be fixed)
                return PatternEncoder.Or(model, $"nn_{empId}_{d:yyyyMMdd}", [dv.Rest!, dv.R1!]);
            }

            var isNormal = dv.FixedState!.Value.IsNormalShift;
            var t = model.NewBoolVar($"nnf_{empId}_{d:yyyyMMdd}");
            model.Add(t == (isNormal ? 0 : 1));
            return t;
        }

        var state = _req.GetFixed(empId, d);
        if (state is null) return null;
        var c = model.NewBoolVar($"nnh_{empId}_{d:yyyyMMdd}");
        model.Add(c == (state.Value.IsNormalShift ? 0 : 1));
        return c;
    }

    private DayState? GetPeriodFixed(string empId, DateOnly d)
    {
        if (_req.FixedAssignments.TryGetValue(empId, out var map) && map.TryGetValue(d, out var s))
            return s;
        // X events fix the start date
        if (_req.XEvents.Any(x => x.EmployeeId == empId && x.StartDate == d))
            return DayState.X;
        return null;
    }
}
