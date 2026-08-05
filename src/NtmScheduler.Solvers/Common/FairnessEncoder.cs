using Google.OrTools.Sat;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Solvers.Common;

/// <summary>
/// Fairness soft rules: per peer-group × intersecting cycle, Σ(max−min).
/// Counts only general rest (R/R*), never R1 (D-18). Extension days excluded (D-13).
/// </summary>
public static class FairnessEncoder
{
    public static IntVar EncodeRestSpread(
        CpModel model,
        string ruleId,
        IReadOnlyList<CycleInfo> cycles,
        DateOnly monthEnd,
        DateOnly periodFirst,
        IReadOnlyList<(string GroupKey, IReadOnlyList<string> MemberIds)> groups,
        Func<string, DateOnly, BoolVar?> restVar,
        Func<string, DateOnly, bool?> fixedIsGeneralRest)
    {
        var terms = new List<IntVar>();
        var intersecting = CycleResolver.Intersecting(cycles, periodFirst, monthEnd);

        foreach (var cycle in intersecting)
        {
            foreach (var (groupKey, members) in groups)
            {
                if (members.Count == 0) continue;
                var counts = new List<IntVar>();
                foreach (var empId in members)
                {
                    var weekdayTerms = new List<LinearExpr>();
                    var to = cycle.End < monthEnd ? cycle.End : monthEnd;
                    for (var d = cycle.Start; d <= to; d = d.AddDays(1))
                    {
                        if (d > monthEnd) break;
                        if (!ScheduleCalendar.IsWeekday(d)) continue;

                        var v = restVar(empId, d);
                        if (v is not null)
                        {
                            weekdayTerms.Add(v);
                            continue;
                        }

                        var fix = fixedIsGeneralRest(empId, d);
                        if (fix == true)
                            weekdayTerms.Add(model.NewConstant(1));
                    }

                    var c = model.NewIntVar(0, Math.Max(1, weekdayTerms.Count),
                        $"fair_wd_{ruleId}_{groupKey}_{empId}_{cycle.Start:yyyyMMdd}");
                    if (weekdayTerms.Count == 0)
                        model.Add(c == 0);
                    else
                        model.Add(c == LinearExpr.Sum(weekdayTerms));
                    counts.Add(c);
                }

                if (counts.Count == 0) continue;
                var max = model.NewIntVar(0, 62, $"fair_max_{ruleId}_{groupKey}_{cycle.Start:yyyyMMdd}");
                var min = model.NewIntVar(0, 62, $"fair_min_{ruleId}_{groupKey}_{cycle.Start:yyyyMMdd}");
                model.AddMaxEquality(max, counts);
                model.AddMinEquality(min, counts);
                var spread = model.NewIntVar(0, 62, $"fair_sp_{ruleId}_{groupKey}_{cycle.Start:yyyyMMdd}");
                model.Add(spread == max - min);
                terms.Add(spread);
            }
        }

        var obj = model.NewIntVar(0, 10_000, $"obj_{ruleId}");
        if (terms.Count == 0) model.Add(obj == 0);
        else model.Add(obj == LinearExpr.Sum(terms));
        return obj;
    }

    public static IntVar EncodeWeekendRestSpread(
        CpModel model,
        string ruleId,
        IReadOnlyList<CycleInfo> cycles,
        DateOnly monthEnd,
        DateOnly periodFirst,
        IReadOnlyList<(string GroupKey, IReadOnlyList<string> MemberIds)> groups,
        Func<string, DateOnly, BoolVar?> restVar,
        Func<string, DateOnly, bool?> fixedIsGeneralRest)
    {
        var terms = new List<IntVar>();
        var intersecting = CycleResolver.Intersecting(cycles, periodFirst, monthEnd);

        foreach (var cycle in intersecting)
        {
            foreach (var (groupKey, members) in groups)
            {
                if (members.Count == 0) continue;
                var counts = new List<IntVar>();
                foreach (var empId in members)
                {
                    var weekendTerms = new List<LinearExpr>();
                    var to = cycle.End < monthEnd ? cycle.End : monthEnd;
                    for (var d = cycle.Start; d <= to; d = d.AddDays(1))
                    {
                        if (d > monthEnd) break;
                        if (!ScheduleCalendar.IsWeekend(d)) continue;

                        var v = restVar(empId, d);
                        if (v is not null)
                        {
                            weekendTerms.Add(v);
                            continue;
                        }

                        var fix = fixedIsGeneralRest(empId, d);
                        if (fix == true)
                            weekendTerms.Add(model.NewConstant(1));
                    }

                    var c = model.NewIntVar(0, Math.Max(1, weekendTerms.Count),
                        $"fair_we_{ruleId}_{groupKey}_{empId}_{cycle.Start:yyyyMMdd}");
                    if (weekendTerms.Count == 0)
                        model.Add(c == 0);
                    else
                        model.Add(c == LinearExpr.Sum(weekendTerms));
                    counts.Add(c);
                }

                if (counts.Count == 0) continue;
                var max = model.NewIntVar(0, 62, $"fair_wemax_{ruleId}_{groupKey}_{cycle.Start:yyyyMMdd}");
                var min = model.NewIntVar(0, 62, $"fair_wemin_{ruleId}_{groupKey}_{cycle.Start:yyyyMMdd}");
                model.AddMaxEquality(max, counts);
                model.AddMinEquality(min, counts);
                var spread = model.NewIntVar(0, 62, $"fair_wesp_{ruleId}_{groupKey}_{cycle.Start:yyyyMMdd}");
                model.Add(spread == max - min);
                terms.Add(spread);
            }
        }

        var obj = model.NewIntVar(0, 10_000, $"obj_{ruleId}");
        if (terms.Count == 0) model.Add(obj == 0);
        else model.Add(obj == LinearExpr.Sum(terms));
        return obj;
    }

    public static IntVar EncodeSupportFair(
        CpModel model,
        IReadOnlyList<CycleInfo> cycles,
        DateOnly monthEnd,
        DateOnly periodFirst,
        IReadOnlyList<(string GroupKey, IReadOnlyList<string> MemberIds)> groups,
        Func<string, DateOnly, BoolVar?> nonHomeWork)
    {
        const string ruleId = "M-S-SUPPORT-FAIR";
        var terms = new List<IntVar>();
        var intersecting = CycleResolver.Intersecting(cycles, periodFirst, monthEnd);

        foreach (var cycle in intersecting)
        {
            foreach (var (groupKey, members) in groups)
            {
                if (members.Count == 0) continue;
                var counts = new List<IntVar>();
                foreach (var empId in members)
                {
                    var supportTerms = new List<LinearExpr>();
                    var to = cycle.End < monthEnd ? cycle.End : monthEnd;
                    for (var d = cycle.Start; d <= to; d = d.AddDays(1))
                    {
                        if (d > monthEnd) break;
                        // History + target month (extension excluded via to ≤ monthEnd). D-13/§6.
                        var v = nonHomeWork(empId, d);
                        if (v is not null) supportTerms.Add(v);
                    }

                    // Also history days in cycle before periodFirst — treated as constants by caller via nonHomeWork returning null;
                    // fixed history non-home counts should be added by caller encoding. For simplicity, scan via optional constant callback.
                    var c = model.NewIntVar(0, Math.Max(1, supportTerms.Count + 62),
                        $"sup_{empId}_{cycle.Start:yyyyMMdd}");
                    if (supportTerms.Count == 0)
                        model.Add(c == 0);
                    else
                        model.Add(c == LinearExpr.Sum(supportTerms));
                    counts.Add(c);
                }

                if (counts.Count == 0) continue;
                var max = model.NewIntVar(0, 62, $"sup_max_{groupKey}_{cycle.Start:yyyyMMdd}");
                var min = model.NewIntVar(0, 62, $"sup_min_{groupKey}_{cycle.Start:yyyyMMdd}");
                model.AddMaxEquality(max, counts);
                model.AddMinEquality(min, counts);
                var spread = model.NewIntVar(0, 62, $"sup_sp_{groupKey}_{cycle.Start:yyyyMMdd}");
                model.Add(spread == max - min);
                terms.Add(spread);
            }
        }

        var obj = model.NewIntVar(0, 10_000, $"obj_{ruleId}");
        if (terms.Count == 0) model.Add(obj == 0);
        else model.Add(obj == LinearExpr.Sum(terms));
        return obj;
    }
}
