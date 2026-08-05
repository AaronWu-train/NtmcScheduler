namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class ScheduleCycle
{
    public long Id { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    /// <summary>General rest (R+R*) quota; default 16.</summary>
    public int RequiredR { get; set; } = 16;
    /// <summary>National-holiday rest (R1) quota.</summary>
    public int RequiredR1 { get; set; }
}
