using NtmScheduler.Core.Domain;

namespace NtmScheduler.Core.Calendar;

public static class CycleResolver
{
    public static CycleInfo? Find(IReadOnlyList<CycleInfo> cycles, DateOnly date) =>
        cycles.FirstOrDefault(c => c.Contains(date));

    public static IReadOnlyList<CycleInfo> Intersecting(
        IReadOnlyList<CycleInfo> cycles,
        DateOnly from,
        DateOnly to) =>
        cycles.Where(c => c.Start <= to && c.End >= from).OrderBy(c => c.Start).ToList();

    public static DateOnly EarliestIntersectingStart(
        IReadOnlyList<CycleInfo> cycles,
        SchedulePeriod period)
    {
        var intersecting = Intersecting(cycles, period.FirstDay, period.RangeEnd);
        if (intersecting.Count == 0)
            throw new InvalidOperationException("排班區間沒有對應的 8 週週期");
        return intersecting[0].Start;
    }
}
