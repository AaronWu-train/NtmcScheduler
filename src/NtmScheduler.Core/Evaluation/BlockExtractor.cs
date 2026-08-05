using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Evaluation;

public sealed record ShiftBlock(string EmployeeId, ShiftType Shift, int Count, bool Closed);

/// <summary>
/// M same-shift blocks: only normal shifts count. R/R*/R1 do not cut; X is skipped.
/// </summary>
public static class BlockExtractor
{
    /// <summary>
    /// Extract same-shift blocks over the target month (history open-block seeded).
    /// Blocks that change shift mid-month are Closed; the month-end tail is Closed=false.
    /// </summary>
    public static IReadOnlyList<ShiftBlock> Extract(ScheduleContext ctx, string employeeId)
    {
        var period = ctx.Period;
        var result = new List<ShiftBlock>();
        ShiftType? current = null;
        var count = 0;

        if (ctx.Histories.TryGetValue(employeeId, out var hist) && hist.OpenBlock is { } open)
        {
            current = open.Shift;
            count = open.Count;
        }

        for (var d = period.FirstDay; d <= period.MonthEnd; d = d.AddDays(1))
        {
            var state = ctx.RequireState(employeeId, d);
            if (state.Type == DayStateType.X || state.IsAnyRest)
                continue;
            if (!state.IsNormalShift)
                continue;

            var shift = state.Shift!.Value;
            if (current is null)
            {
                current = shift;
                count = 1;
            }
            else if (current == shift)
            {
                count++;
            }
            else
            {
                result.Add(new ShiftBlock(employeeId, current.Value, count, Closed: true));
                current = shift;
                count = 1;
            }
        }

        if (current is not null)
            result.Add(new ShiftBlock(employeeId, current.Value, count, Closed: false));

        return result;
    }

    public static int BlockDeviation(ScheduleContext ctx, string employeeId)
    {
        var period = ctx.Period;
        var total = 0;
        ShiftType? current = null;
        var count = 0;

        // Seed from open historical block.
        if (ctx.Histories.TryGetValue(employeeId, out var hist) && hist.OpenBlock is { } open)
        {
            current = open.Shift;
            count = open.Count;
        }

        for (var d = period.FirstDay; d <= period.MonthEnd; d = d.AddDays(1))
        {
            var state = ctx.RequireState(employeeId, d);
            if (state.Type == DayStateType.X || state.IsAnyRest)
                continue; // skip X; R/R*/R1 do not cut

            if (!state.IsNormalShift)
                continue;

            var shift = state.Shift!.Value;
            if (current is null)
            {
                current = shift;
                count = 1;
            }
            else if (current == shift)
            {
                count++;
            }
            else
            {
                total += SegmentExtractor.Deviation(count);
                current = shift;
                count = 1;
            }
        }

        // Unfinished at month end: only excess over 5.
        if (current is not null)
            total += Math.Max(0, count - 5);

        // Also account for blocks that closed when we switched — already counted.
        // If a block closed mid-month via shift change, Deviation already applied.
        // Re-walk to count closed mid-month blocks properly was done above.

        return total;
    }

    /// <summary>
    /// Full deviation including properly closed mid-month blocks (on shift change).
    /// The above method already adds Deviation on shift change.
    /// </summary>
    public static int Evaluate(ScheduleContext ctx, string employeeId) =>
        BlockDeviation(ctx, employeeId);
}
