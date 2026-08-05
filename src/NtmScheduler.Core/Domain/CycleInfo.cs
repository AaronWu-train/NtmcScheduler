namespace NtmScheduler.Core.Domain;

/// <summary>
/// 8-week leave cycle. RequiredR = general rest (R+R*) quota (default 16).
/// RequiredR1 = national holiday count for R1 quota.
/// </summary>
public sealed record CycleInfo(
    DateOnly Start,
    DateOnly End,
    int RequiredR,
    int RequiredR1)
{
    public int CycleDays => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public int FutureDaysAfter(DateOnly monthEnd)
    {
        if (monthEnd >= End) return 0;
        var firstFuture = monthEnd.AddDays(1);
        if (firstFuture < Start) firstFuture = Start;
        if (firstFuture > End) return 0;
        return End.DayNumber - firstFuture.DayNumber + 1;
    }

    public int ReservedGeneralRest(DateOnly monthEnd)
    {
        var future = FutureDaysAfter(monthEnd);
        if (future <= 0) return 0;
        return (int)Math.Ceiling(RequiredR * (double)future / CycleDays);
    }
}
