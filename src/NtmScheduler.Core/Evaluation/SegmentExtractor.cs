using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation;

public sealed record WorkSegment(string EmployeeId, DateOnly Start, DateOnly End, int Length, bool Closed);

/// <summary>
/// Continuous work segments: consecutive calendar days that are work (Shift/X).
/// Any rest (R, R*, R1) ends a segment. Scoring per D-13 uses MonthEnd as the tail boundary.
/// </summary>
public static class SegmentExtractor
{
    public static IReadOnlyList<WorkSegment> Extract(
        ScheduleContext ctx,
        string employeeId,
        bool scoreOnlyTargetMonth = true)
    {
        var period = ctx.Period;
        var result = new List<WorkSegment>();
        DateOnly? segStart = null;
        var length = 0;

        // Walk from history into the period to continue open segments.
        var historyStart = ctx.Histories.TryGetValue(employeeId, out var hist) && hist.Days.Count > 0
            ? hist.Days.Keys.Min()
            : period.FirstDay;

        for (var d = historyStart; d <= period.RangeEnd; d = d.AddDays(1))
        {
            var state = ctx.GetState(employeeId, d);
            var isWork = state?.IsWorkDay == true;

            if (isWork)
            {
                segStart ??= d;
                length++;
            }
            else if (segStart is not null)
            {
                // Segment ends on any non-work (including R1).
                var end = d.AddDays(-1);
                var closedInTarget = !scoreOnlyTargetMonth || end <= period.MonthEnd;
                if (closedInTarget && (segStart.Value <= period.MonthEnd || !scoreOnlyTargetMonth))
                {
                    // Per D-13: if segment ends after MonthEnd but started before/on MonthEnd,
                    // treat as unfinished for scoring (do not score shortness).
                    if (scoreOnlyTargetMonth && end > period.MonthEnd)
                    {
                        // Unfinished at month end — scored separately via tail excess.
                    }
                    else if (segStart.Value <= period.RangeEnd)
                    {
                        var scoredLength = length;
                        if (scoreOnlyTargetMonth && segStart.Value < period.FirstDay)
                        {
                            // Full length includes history prefix for closed segments that end in target month.
                            scoredLength = length;
                        }
                        result.Add(new WorkSegment(employeeId, segStart.Value, end, scoredLength, Closed: true));
                    }
                }
                segStart = null;
                length = 0;
            }
        }

        // Tail still open at RangeEnd — unfinished.
        if (segStart is not null)
        {
            result.Add(new WorkSegment(employeeId, segStart.Value, period.RangeEnd, length, Closed: false));
        }

        return result;
    }

    /// <summary>
    /// Deviation D(L) for closed segments ending on or before MonthEnd.
    /// Unfinished tails (still working on MonthEnd) only contribute max(0, L_to_month_end - 5).
    /// </summary>
    public static int StreakDeviation(ScheduleContext ctx, string employeeId)
    {
        var period = ctx.Period;
        var total = 0;
        DateOnly? segStart = null;
        var length = 0;

        var historyStart = ctx.Histories.TryGetValue(employeeId, out var hist) && hist.Days.Count > 0
            ? hist.Days.Keys.Min()
            : period.FirstDay;

        for (var d = historyStart; d <= period.MonthEnd; d = d.AddDays(1))
        {
            var state = ctx.GetState(employeeId, d);
            var isWork = state?.IsWorkDay == true;
            if (isWork)
            {
                segStart ??= d;
                length++;
            }
            else if (segStart is not null)
            {
                // Closed within target month (or with history prefix).
                if (d.AddDays(-1) >= period.FirstDay || segStart < period.FirstDay)
                    total += Deviation(length);
                segStart = null;
                length = 0;
            }
        }

        // Unfinished at MonthEnd: only excess over 5.
        if (segStart is not null)
            total += Math.Max(0, length - 5);

        return total;
    }

    public static int Deviation(int length) =>
        Math.Max(0, 3 - length) + Math.Max(0, length - 5);
}
