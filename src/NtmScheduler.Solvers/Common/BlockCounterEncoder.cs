using Google.OrTools.Sat;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Solvers.Common;

/// <summary>
/// M-S-BLOCK channeling: blockShift / blockLen with close-event scoring (D-18: R1 does not cut).
/// </summary>
public static class BlockCounterEncoder
{
    // 0=none, 1=Morning, 2=Afternoon, 3=Night
    public static int ShiftCode(ShiftType s) => s switch
    {
        ShiftType.Morning => 1,
        ShiftType.Afternoon => 2,
        ShiftType.Night => 3,
        _ => 0
    };

    public static IntVar Encode(
        CpModel model,
        string empId,
        IReadOnlyList<DateOnly> days,
        DateOnly monthEnd,
        Func<DateOnly, BoolVar?> isNormalShiftMorning,
        Func<DateOnly, BoolVar?> isNormalShiftAfternoon,
        Func<DateOnly, BoolVar?> isNormalShiftNight,
        Func<DateOnly, bool> isFixedSkip, // fixed X or known non-normal that continues block
        (ShiftType Shift, int Count)? openBlock,
        out IntVar objective)
    {
        var maxLen = Math.Max(62, days.Count + (openBlock?.Count ?? 0) + 1);
        var dTable = new long[maxLen + 1];
        dTable[0] = 0;
        for (var L = 1; L <= maxLen; L++)
            dTable[L] = PatternEncoder.Deviation(L);

        IntVar? prevShift = null;
        IntVar? prevLen = null;
        var scoreTerms = new List<LinearExpr>();

        if (openBlock is { } ob)
        {
            prevShift = model.NewConstant(ShiftCode(ob.Shift));
            prevLen = model.NewConstant(ob.Count);
        }
        else
        {
            prevShift = model.NewConstant(0);
            prevLen = model.NewConstant(0);
        }

        foreach (var d in days)
        {
            var m = isNormalShiftMorning(d);
            var a = isNormalShiftAfternoon(d);
            var n = isNormalShiftNight(d);

            // hasNormal = OR of available shift indicators (null => false)
            var normalLits = new List<BoolVar>();
            if (m is not null) normalLits.Add(m);
            if (a is not null) normalLits.Add(a);
            if (n is not null) normalLits.Add(n);

            BoolVar hasNormal;
            if (normalLits.Count == 0)
            {
                hasNormal = model.NewBoolVar($"blk_has_{empId}_{d:yyyyMMdd}");
                model.Add(hasNormal == 0);
            }
            else
            {
                hasNormal = PatternEncoder.Or(model, $"blk_has_{empId}_{d:yyyyMMdd}", normalLits);
            }

            var curShift = model.NewIntVar(0, 3, $"blk_s_{empId}_{d:yyyyMMdd}");
            var curLen = model.NewIntVar(0, maxLen, $"blk_l_{empId}_{d:yyyyMMdd}");

            // No normal shift (R/R*/R1/X): carry forward.
            model.Add(curShift == prevShift).OnlyEnforceIf(hasNormal.Not());
            model.Add(curLen == prevLen).OnlyEnforceIf(hasNormal.Not());

            // Which shift today?
            if (m is not null) model.Add(curShift == 1).OnlyEnforceIf(m);
            if (a is not null) model.Add(curShift == 2).OnlyEnforceIf(a);
            if (n is not null) model.Add(curShift == 3).OnlyEnforceIf(n);

            // Same as previous → len+1; else close previous (if prevLen>0) and reset to 1.
            var same = model.NewBoolVar($"blk_same_{empId}_{d:yyyyMMdd}");
            model.Add(curShift == prevShift).OnlyEnforceIf(new[] { hasNormal, same });
            model.Add(curShift != prevShift).OnlyEnforceIf(new[] { hasNormal, same.Not() });

            model.Add(curLen == prevLen + 1).OnlyEnforceIf(new[] { hasNormal, same });
            model.Add(curLen == 1).OnlyEnforceIf(new[] { hasNormal, same.Not() });

            // Close event when switching and previous block existed, only if d <= monthEnd.
            if (d <= monthEnd)
            {
                var closing = model.NewBoolVar($"blk_close_{empId}_{d:yyyyMMdd}");
                var prevPositive = model.NewBoolVar($"blk_pp_{empId}_{d:yyyyMMdd}");
                model.Add(prevLen >= 1).OnlyEnforceIf(prevPositive);
                model.Add(prevLen < 1).OnlyEnforceIf(prevPositive.Not());

                // closing ↔ hasNormal ∧ ¬same ∧ prevPositive
                var notSame = PatternEncoder.NotAsBool(model, same, $"blk_ns_{empId}_{d:yyyyMMdd}");
                var closeAnd = PatternEncoder.And(model, $"blk_ca_{empId}_{d:yyyyMMdd}",
                    [hasNormal, notSame, prevPositive]);
                model.Add(closing == closeAnd);

                var closeScore = model.NewIntVar(0, 10, $"blk_sc_{empId}_{d:yyyyMMdd}");
                model.AddElement(prevLen, dTable, closeScore);
                var scored = model.NewIntVar(0, 10, $"blk_ss_{empId}_{d:yyyyMMdd}");
                model.Add(scored == closeScore).OnlyEnforceIf(closing);
                model.Add(scored == 0).OnlyEnforceIf(closing.Not());
                scoreTerms.Add(scored);
            }

            _ = isFixedSkip; // reserved for fixed-day constant path (caller inlines constants)
            prevShift = curShift;
            prevLen = curLen;
        }

        // Tail excess at month end.
        var monthEndDay = days.LastOrDefault(d => d <= monthEnd);
        if (monthEndDay != default && prevLen is not null)
        {
            // Find len at monthEnd — prevLen after processing monthEndDay is that value.
            var excess = model.NewIntVar(0, maxLen, $"blk_tail_{empId}");
            model.Add(excess >= prevLen - 5);
            model.Add(excess >= 0);
            // excess == max(0, prevLen-5)
            var ge = model.NewBoolVar($"blk_tail_ge_{empId}");
            model.Add(prevLen >= 5).OnlyEnforceIf(ge);
            model.Add(prevLen < 5).OnlyEnforceIf(ge.Not());
            model.Add(excess == prevLen - 5).OnlyEnforceIf(ge);
            model.Add(excess == 0).OnlyEnforceIf(ge.Not());
            scoreTerms.Add(excess);
        }

        objective = model.NewIntVar(0, days.Count * 10 + 10, $"obj_block_{empId}");
        if (scoreTerms.Count == 0)
            model.Add(objective == 0);
        else
            model.Add(objective == LinearExpr.Sum(scoreTerms));
        return objective;
    }
}
