using NtmScheduler.Core.Domain;

namespace NtmScheduler.Solvers.Common;

/// <summary>
/// Computes GEN-H-03 forbidden shift pairs from configured shift times (not hard-coded).
/// </summary>
public static class RestGapAnalyzer
{
    public const double MinHours = 11;

    public readonly record struct ShiftOccurrence(DateOnly Date, ShiftType Shift);

    public readonly record struct ForbiddenPair(
        ShiftOccurrence First,
        ShiftOccurrence Second);

    /// <summary>
    /// All (date,shift)×(date,shift) pairs within ±lookAheadDays that violate the 11h gap.
    /// </summary>
    public static IReadOnlyList<ForbiddenPair> ForbiddenNormalPairs(
        Unit unit,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        int lookAheadDays = 2)
    {
        var shifts = new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night };
        var result = new List<ForbiddenPair>();

        for (var d1 = rangeStart; d1 <= rangeEnd; d1 = d1.AddDays(1))
        {
            foreach (var s1 in shifts)
            {
                var (_, end1) = ShiftTimeConfig.Interval(unit, d1, s1);
                var maxD2 = d1.AddDays(lookAheadDays);
                if (maxD2 > rangeEnd) maxD2 = rangeEnd;

                for (var d2 = d1; d2 <= maxD2; d2 = d2.AddDays(1))
                {
                    foreach (var s2 in shifts)
                    {
                        if (d1 == d2 && Order(s1) >= Order(s2)) continue;
                        var (start2, _) = ShiftTimeConfig.Interval(unit, d2, s2);
                        if ((start2 - end1).TotalHours < MinHours)
                        {
                            result.Add(new ForbiddenPair(
                                new ShiftOccurrence(d1, s1),
                                new ShiftOccurrence(d2, s2)));
                        }
                    }
                }
            }
        }

        return result;
    }

    public static bool Violates(DateTime previousEnd, DateTime nextStart) =>
        (nextStart - previousEnd).TotalHours < MinHours;

    public static bool ViolatesShiftPair(
        Unit unit, DateOnly d1, ShiftType s1, DateOnly d2, ShiftType s2)
    {
        var (_, end1) = ShiftTimeConfig.Interval(unit, d1, s1);
        var (start2, _) = ShiftTimeConfig.Interval(unit, d2, s2);
        return Violates(end1, start2);
    }

    private static int Order(ShiftType s) => s switch
    {
        ShiftType.Morning => 0,
        ShiftType.Afternoon => 1,
        ShiftType.Night => 2,
        _ => 3
    };
}
