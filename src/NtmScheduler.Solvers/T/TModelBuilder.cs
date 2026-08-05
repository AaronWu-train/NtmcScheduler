using Google.OrTools.Sat;
using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Domain;
using NtmScheduler.Solvers.Common;
namespace NtmScheduler.Solvers.T;

public sealed class TModelBuilder
{
    private readonly SolveRequestView _req;

    public TModelBuilder(SolveRequest request)
    {
        _req = new SolveRequestView { Request = request };
    }

    public BuiltModel Build()
    {
        var model = new CpModel();
        var days = new Dictionary<(string Emp, DateOnly Date), EmployeeDayVars>();
        var built = new BuiltModel { Model = model, Days = days, Request = _req };

        CreateVariables(model, days);
        HardConstraintEncoder.EncodeContinuousWork(model, _req, days);
        HardConstraintEncoder.EncodeRestGap(model, _req, days);
        HardConstraintEncoder.EncodeCycleRest(model, _req, days);
        EncodeSoftObjectives(model, days, built);
        return built;
    }

    private void CreateVariables(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        foreach (var emp in _req.Employees)
        {
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
                var work = model.NewBoolVar($"work_{emp.Id}_{d:yyyyMMdd}");
                var shift = _req.ResolveTShift(emp.Id, d);

                var dv = new EmployeeDayVars
                {
                    EmployeeId = emp.Id,
                    Date = d,
                    Rest = rest,
                    R1 = r1,
                    WorkAny = work
                };
                dv.Work[(string.Empty, shift)] = work;
                dv.ShiftOr[shift] = work;

                model.Add(work + rest + r1 == 1);
                days[(emp.Id, d)] = dv;
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
                "T-S-ATTEND" => EncodeAttend(model, days),
                "T-S-SPECIALTY" => EncodeSpecialty(model, days),
                "T-S-ABILITY" => EncodeAbility(model, days),
                "GEN-S-STREAK" => EncodeStreak(model, days),
                "T-S-MONTH-REST" => EncodeMonthRest(model, days),
                "T-S-MONTH-BALANCE" => EncodeMonthBalance(model, days),
                "GEN-S-WEEKDAY-R" => EncodeFairnessWeekday(model, days),
                "GEN-S-WEEKEND-R" => EncodeFairnessWeekend(model, days),
                _ => model.NewConstant(0)
            };
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

            terms.Add(PatternEncoder.OneMinus(dv.Rest!));
        }

        var obj = model.NewIntVar(0, Math.Max(1, terms.Count), "obj_GEN-R-01");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private IntVar EncodeAttend(CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var terms = new List<IntVar>();
        if (_req.Request.MonthlyShifts is null)
            return model.NewConstant(0);

        foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
        {
            var members = _req.Employees
                .Where(e => _req.Request.MonthlyShifts.TryGetValue(e.Id, out var s) && s == shift)
                .ToList();
            var target = members.Count / 2;

            foreach (var d in _req.Period.TargetMonthDays)
            {
                var workTerms = new List<LinearExpr>();
                foreach (var emp in members)
                {
                    if (!days.TryGetValue((emp.Id, d), out var dv)) continue;
                    if (dv.IsFixed)
                    {
                        if (dv.FixedState!.Value.IsNormalShift)
                            workTerms.Add(model.NewConstant(1));
                        continue;
                    }

                    workTerms.Add(dv.WorkAny!);
                }

                var attend = model.NewIntVar(0, Math.Max(1, members.Count), $"att_{shift}_{d:yyyyMMdd}");
                model.Add(attend == (workTerms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(workTerms)));
                var shortfall = model.NewIntVar(0, target, $"short_{shift}_{d:yyyyMMdd}");
                model.Add(shortfall >= target - attend);
                model.Add(shortfall >= 0);
                // shortfall == max(0, target - attend)
                var ge = model.NewBoolVar($"att_ge_{shift}_{d:yyyyMMdd}");
                model.Add(attend <= target).OnlyEnforceIf(ge);
                model.Add(attend > target).OnlyEnforceIf(ge.Not());
                model.Add(shortfall == target - attend).OnlyEnforceIf(ge);
                model.Add(shortfall == 0).OnlyEnforceIf(ge.Not());
                terms.Add(shortfall);
            }
        }

        var obj = model.NewIntVar(0, 10_000, "obj_T-S-ATTEND");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private IntVar EncodeSpecialty(CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var terms = new List<LinearExpr>();
        if (_req.Request.MonthlyShifts is null)
            return model.NewConstant(0);

        foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
        {
            var members = _req.Employees
                .Where(e => _req.Request.MonthlyShifts.TryGetValue(e.Id, out var s) && s == shift)
                .ToList();
            var specialties = members
                .Select(m => m.Specialty)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .Cast<string>()
                .ToList();

            foreach (var d in _req.Period.TargetMonthDays)
            {
                foreach (var sp in specialties)
                {
                    var holders = members.Where(m => m.Specialty == sp).ToList();
                    var workLits = new List<BoolVar>();
                    foreach (var emp in holders)
                    {
                        if (!days.TryGetValue((emp.Id, d), out var dv)) continue;
                        if (dv.IsFixed)
                        {
                            if (dv.FixedState!.Value.IsNormalShift)
                            {
                                var t = model.NewBoolVar($"spf_{emp.Id}_{d:yyyyMMdd}");
                                model.Add(t == 1);
                                workLits.Add(t);
                            }
                            continue;
                        }

                        workLits.Add(dv.WorkAny!);
                    }

                    if (workLits.Count == 0)
                    {
                        terms.Add(model.NewConstant(1));
                        continue;
                    }

                    var any = PatternEncoder.Or(model, $"sp_{sp}_{shift}_{d:yyyyMMdd}", workLits);
                    terms.Add(PatternEncoder.OneMinus(any));
                }
            }
        }

        var obj = model.NewIntVar(0, Math.Max(1, terms.Count), "obj_T-S-SPECIALTY");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private IntVar EncodeAbility(CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var terms = new List<IntVar>();
        if (_req.Request.MonthlyShifts is null)
            return model.NewConstant(0);

        foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
        {
            var members = _req.Employees
                .Where(e => _req.Request.MonthlyShifts.TryGetValue(e.Id, out var s) && s == shift)
                .ToList();

            foreach (var d in _req.Period.TargetMonthDays)
            {
                LinearExpr abilitySum = model.NewConstant(0);
                LinearExpr attendSum = model.NewConstant(0);
                var first = true;
                foreach (var emp in members)
                {
                    BoolVar? work = null;
                    if (days.TryGetValue((emp.Id, d), out var dv))
                    {
                        if (dv.IsFixed)
                        {
                            if (!dv.FixedState!.Value.IsNormalShift) continue;
                            work = model.NewBoolVar($"abf_{emp.Id}_{d:yyyyMMdd}");
                            model.Add(work == 1);
                        }
                        else work = dv.WorkAny;
                    }

                    if (work is null) continue;
                    var ab = emp.Ability ?? 0;
                    if (first)
                    {
                        abilitySum = work * ab;
                        attendSum = work;
                        first = false;
                    }
                    else
                    {
                        abilitySum += work * ab;
                        attendSum += work;
                    }
                }

                var def = model.NewIntVar(0, members.Count * 3, $"def_{shift}_{d:yyyyMMdd}");
                model.Add(def >= 3 * attendSum - abilitySum);
                model.Add(def >= 0);
                // Tighten equality to max(0, ...)
                var pos = model.NewBoolVar($"defpos_{shift}_{d:yyyyMMdd}");
                model.Add(3 * attendSum - abilitySum >= 0).OnlyEnforceIf(pos);
                model.Add(3 * attendSum - abilitySum < 0).OnlyEnforceIf(pos.Not());
                model.Add(def == 3 * attendSum - abilitySum).OnlyEnforceIf(pos);
                model.Add(def == 0).OnlyEnforceIf(pos.Not());
                terms.Add(def);
            }
        }

        var obj = model.NewIntVar(0, 10_000, "obj_T-S-ABILITY");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private IntVar EncodeStreak(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days) =>
        new MStreakHelper(_req, model, days).Encode();

    private IntVar EncodeMonthRest(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        var terms = new List<IntVar>();
        if (_req.Request.MonthlyShifts is null || _req.Request.PreviousMonthShifts is null)
            return model.NewConstant(0);

        foreach (var emp in _req.Employees)
        {
            if (!_req.Request.PreviousMonthShifts.TryGetValue(emp.Id, out var prev) || prev != ShiftType.Night)
                continue;
            if (!_req.Request.MonthlyShifts.TryGetValue(emp.Id, out var cur) || cur != ShiftType.Morning)
                continue;

            // Find last night in history and first morning in target month — constants from history,
            // first morning is decision-dependent: enumerate possible first-morning days.
            DateOnly? lastNight = null;
            var histStart = _req.Histories.TryGetValue(emp.Id, out var hist) && hist.Days.Count > 0
                ? hist.Days.Keys.Min()
                : _req.Period.FirstDay.AddDays(-31);
            for (var d = histStart; d < _req.Period.FirstDay; d = d.AddDays(1))
            {
                var s = _req.GetFixed(emp.Id, d);
                if (s?.IsNormalShift == true && s.Value.Shift == ShiftType.Night)
                    lastNight = d;
            }

            if (lastNight is null) continue;

            // For each possible first morning day f, if work[f] and no morning work before f, score max(0,2-n)
            foreach (var f in _req.Period.TargetMonthDays)
            {
                if (!days.TryGetValue((emp.Id, f), out var dv) || dv.IsFixed) continue;
                var isFirst = new List<BoolVar> { dv.WorkAny! };
                foreach (var b in _req.Period.TargetMonthDays.Where(x => x < f))
                {
                    if (!days.TryGetValue((emp.Id, b), out var bdv) || bdv.IsFixed) continue;
                    isFirst.Add(CreateNot(model, bdv.WorkAny!));
                }

                // Count R between lastNight and f
                var restCount = 0;
                var restVars = new List<LinearExpr>();
                for (var d = lastNight.Value.AddDays(1); d < f; d = d.AddDays(1))
                {
                    if (days.TryGetValue((emp.Id, d), out var md) && !md.IsFixed)
                        restVars.Add(md.Rest!);
                    else if (_req.GetFixed(emp.Id, d)?.IsGeneralRest == true)
                        restCount++;
                }

                var n = model.NewIntVar(0, 62, $"mr_n_{emp.Id}_{f:yyyyMMdd}");
                model.Add(n == restCount + (restVars.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(restVars)));

                var firstLit = PatternEncoder.And(model, $"mr_first_{emp.Id}_{f:yyyyMMdd}",
                    isFirst.Select(x => x).ToList());
                var deficit = model.NewIntVar(0, 2, $"mr_def_{emp.Id}_{f:yyyyMMdd}");
                model.Add(deficit >= 2 - n);
                model.Add(deficit >= 0);
                var scored = model.NewIntVar(0, 2, $"mr_sc_{emp.Id}_{f:yyyyMMdd}");
                model.Add(scored == deficit).OnlyEnforceIf(firstLit);
                model.Add(scored == 0).OnlyEnforceIf(firstLit.Not());
                terms.Add(scored);
            }
        }

        var obj = model.NewIntVar(0, Math.Max(1, terms.Count * 2), "obj_T-S-MONTH-REST");
        model.Add(obj == (terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms)));
        return obj;
    }

    private static BoolVar CreateNot(CpModel model, BoolVar v)
    {
        var n = model.NewBoolVar($"not_{v.GetIndex()}");
        model.Add(n + v == 1);
        return n;
    }

    private IntVar EncodeMonthBalance(
        CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        if (_req.Request.MonthlyShifts is null || _req.Request.PreviousMonthShifts is null)
            return model.NewConstant(0);

        var rotators = _req.Employees.Where(e =>
            _req.Request.PreviousMonthShifts.TryGetValue(e.Id, out var p) && p == ShiftType.Night &&
            _req.Request.MonthlyShifts.TryGetValue(e.Id, out var c) && c == ShiftType.Morning).ToList();

        if (rotators.Count == 0)
            return model.NewConstant(0);

        var prevDay = _req.Period.FirstDay.AddDays(-1);
        var firstDay = _req.Period.FirstDay;

        LinearExpr CountRest(DateOnly d, bool preferFixed)
        {
            var terms = new List<LinearExpr>();
            foreach (var emp in rotators)
            {
                if (days.TryGetValue((emp.Id, d), out var dv) && !dv.IsFixed)
                {
                    terms.Add(dv.Rest!);
                    continue;
                }

                var s = _req.GetFixed(emp.Id, d);
                if (s?.IsGeneralRest == true)
                    terms.Add(model.NewConstant(1));
            }

            return terms.Count == 0 ? model.NewConstant(0) : LinearExpr.Sum(terms);
        }

        var a = model.NewIntVar(0, rotators.Count, "mb_a");
        var b = model.NewIntVar(0, rotators.Count, "mb_b");
        model.Add(a == CountRest(prevDay, true));
        model.Add(b == CountRest(firstDay, false));
        var diff = model.NewIntVar(0, rotators.Count, "mb_diff");
        // |a-b|
        var ge = model.NewBoolVar("mb_ge");
        model.Add(a >= b).OnlyEnforceIf(ge);
        model.Add(a < b).OnlyEnforceIf(ge.Not());
        model.Add(diff == a - b).OnlyEnforceIf(ge);
        model.Add(diff == b - a).OnlyEnforceIf(ge.Not());
        return diff;
    }

    private IntVar EncodeFairnessWeekday(
        CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        if (_req.Request.MonthlyShifts is null)
            return model.NewConstant(0);
        var groups = _req.Employees
            .GroupBy(e => _req.Request.MonthlyShifts.TryGetValue(e.Id, out var s) ? s : (ShiftType?)null)
            .Where(g => g.Key is not null)
            .Select(g => (g.Key!.Value.ToDisplay(), (IReadOnlyList<string>)g.Select(e => e.Id).ToList()))
            .ToList();
        return FairnessEncoder.EncodeRestSpread(
            model, "GEN-S-WEEKDAY-R", _req.Cycles, _req.Period.MonthEnd, _req.Period.FirstDay, groups,
            (emp, d) => days.TryGetValue((emp, d), out var dv) && !dv.IsFixed ? dv.Rest : null,
            (emp, d) => _req.GetFixed(emp, d)?.IsGeneralRest);
    }

    private IntVar EncodeFairnessWeekend(
        CpModel model, Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        if (_req.Request.MonthlyShifts is null)
            return model.NewConstant(0);
        var groups = _req.Employees
            .GroupBy(e => _req.Request.MonthlyShifts.TryGetValue(e.Id, out var s) ? s : (ShiftType?)null)
            .Where(g => g.Key is not null)
            .Select(g => (g.Key!.Value.ToDisplay(), (IReadOnlyList<string>)g.Select(e => e.Id).ToList()))
            .ToList();
        return FairnessEncoder.EncodeWeekendRestSpread(
            model, "GEN-S-WEEKEND-R", _req.Cycles, _req.Period.MonthEnd, _req.Period.FirstDay, groups,
            (emp, d) => days.TryGetValue((emp, d), out var dv) && !dv.IsFixed ? dv.Rest : null,
            (emp, d) => _req.GetFixed(emp, d)?.IsGeneralRest);
    }

    private DayState? GetPeriodFixed(string empId, DateOnly d)
    {
        if (_req.FixedAssignments.TryGetValue(empId, out var map) && map.TryGetValue(d, out var s))
            return s;
        if (_req.XEvents.Any(x => x.EmployeeId == empId && x.StartDate == d))
            return DayState.X;
        return null;
    }
}

/// <summary>Shared streak encoder for T (mirrors M GEN-S-STREAK).</summary>
file sealed class MStreakHelper
{
    private readonly SolveRequestView _req;
    private readonly CpModel _model;
    private readonly Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> _days;

    public MStreakHelper(
        SolveRequestView req, CpModel model,
        Dictionary<(string Emp, DateOnly Date), EmployeeDayVars> days)
    {
        _req = req;
        _model = model;
        _days = days;
    }

    public IntVar Encode()
    {
        var scores = new List<IntVar>();
        foreach (var emp in _req.Employees)
            scores.Add(EncodeEmp(emp.Id));
        var obj = _model.NewIntVar(0, 10_000, "obj_GEN-S-STREAK");
        _model.Add(obj == LinearExpr.Sum(scores));
        return obj;
    }

    private IntVar EncodeEmp(string empId)
    {
        var monthEnd = _req.Period.MonthEnd;
        var histStart = _req.Histories.TryGetValue(empId, out var hist) && hist.Days.Count > 0
            ? hist.Days.Keys.Min()
            : _req.Period.FirstDay;

        BoolVar IsWork(DateOnly d)
        {
            if (_days.TryGetValue((empId, d), out var dv))
            {
                if (dv.IsFixed)
                {
                    var t = _model.NewBoolVar($"tsw_{empId}_{d:yyyyMMdd}");
                    _model.Add(t == (dv.FixedState!.Value.IsWorkDay ? 1 : 0));
                    return t;
                }
                return dv.WorkAny!;
            }

            var state = _req.GetFixed(empId, d);
            var c = _model.NewBoolVar($"tswh_{empId}_{d:yyyyMMdd}");
            _model.Add(c == (state?.IsWorkDay == true ? 1 : 0));
            return c;
        }

        BoolVar IsBoundary(DateOnly d)
        {
            if (_days.TryGetValue((empId, d), out var dv))
            {
                if (dv.IsFixed)
                {
                    var t = _model.NewBoolVar($"tsb_{empId}_{d:yyyyMMdd}");
                    _model.Add(t == (dv.FixedState!.Value.IsAnyRest ? 1 : 0));
                    return t;
                }
                return PatternEncoder.Or(_model, $"tsb_{empId}_{d:yyyyMMdd}", [dv.Rest!, dv.R1!]);
            }

            var state = _req.GetFixed(empId, d);
            var c = _model.NewBoolVar($"tsbh_{empId}_{d:yyyyMMdd}");
            _model.Add(c == (state?.IsAnyRest == true ? 1 : 0));
            return c;
        }

        var scores = new List<LinearExpr>();
        for (var end = _req.Period.FirstDay; end <= monthEnd; end = end.AddDays(1))
        {
            foreach (var L in new[] { 1, 2, 6 })
            {
                var start = end.AddDays(1 - L);
                if (start < histStart) continue;
                var after = end.AddDays(1);
                if (after > monthEnd) continue;

                var lits = new List<BoolVar>();
                var before = start.AddDays(-1);
                if (before >= histStart)
                    lits.Add(IsBoundary(before));
                for (var i = 0; i < L; i++)
                    lits.Add(IsWork(start.AddDays(i)));
                lits.Add(IsBoundary(after));

                var ind = PatternEncoder.And(_model, $"tstreak_{empId}_{end:yyyyMMdd}_{L}", lits);
                var w = L switch { 1 => 2, 2 => 1, 6 => 1, _ => 0 };
                var term = _model.NewIntVar(0, w, $"tst_{empId}_{end:yyyyMMdd}_{L}");
                _model.Add(term == w).OnlyEnforceIf(ind);
                _model.Add(term == 0).OnlyEnforceIf(ind.Not());
                scores.Add(term);
            }
        }

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
            var ind = PatternEncoder.And(_model, $"ttail_{empId}_{L}", lits);
            var excess = L - 5;
            var term = _model.NewIntVar(0, excess, $"ttt_{empId}_{L}");
            _model.Add(term == excess).OnlyEnforceIf(ind);
            _model.Add(term == 0).OnlyEnforceIf(ind.Not());
            scores.Add(term);
        }

        var obj = _model.NewIntVar(0, 500, $"obj_tstreak_{empId}");
        _model.Add(obj == (scores.Count == 0 ? _model.NewConstant(0) : LinearExpr.Sum(scores)));
        return obj;
    }
}
