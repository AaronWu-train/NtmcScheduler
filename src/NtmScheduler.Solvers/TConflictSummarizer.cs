using NtmScheduler.Core.Abstractions.Dtos;
using NtmScheduler.Core.Calendar;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Solvers;

/// <summary>
/// T INFEASIBLE summary (D-11): cycle stats, group sizes, R* count, text explanation.
/// </summary>
public static class TConflictSummarizer
{
    public static TConflictSummaryDto Summarize(SolveRequest request)
    {
        var cycles = CycleResolver.Intersecting(
            request.Cycles, request.Period.FirstDay, request.Period.RangeEnd);

        var cycleStats = cycles.Select(c =>
        {
            var remaining = c.End > request.Period.RangeEnd
                ? c.End.DayNumber - request.Period.RangeEnd.DayNumber
                : 0;
            return new CycleRestStatDto(
                c.Start, c.End, c.RequiredR, c.RequiredR1, remaining, request.Employees.Count);
        }).ToList();

        var groupSizes = new Dictionary<ShiftType, int>();
        if (request.MonthlyShifts is not null)
        {
            foreach (var shift in new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night })
            {
                groupSizes[shift] = request.Employees.Count(e =>
                    request.MonthlyShifts.TryGetValue(e.Id, out var s) && s == shift);
            }
        }

        var rStar = request.RStarRequests.Count;
        var msg =
            $"檢測單位嚴格模型無解。人員 {request.Employees.Count} 人，R* 請求 {rStar} 筆；" +
            $"相交週期 {cycleStats.Count} 個。請檢查 GEN-H-04 休假額度、GEN-H-02 連續工作與歷史銜接。";

        return new TConflictSummaryDto
        {
            Message = msg,
            CycleStats = cycleStats,
            GroupSizes = groupSizes,
            TotalRStarRequests = rStar
        };
    }
}
