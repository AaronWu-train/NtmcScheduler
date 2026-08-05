using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Data.Entities;

/// <summary>Immutable schedule snapshot (history import or manual save-point).</summary>
public sealed class ScheduleSnapshot
{
    public long Id { get; set; }
    public Unit Unit { get; set; }
    /// <summary>yyyy-MM</summary>
    public string Month { get; set; } = "";
    public int VersionNo { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Operator { get; set; } = "";
    /// <summary>True for the latest imported/restored history snapshot of that month (solver history source when no MonthSchedule).</summary>
    public bool IsCurrent { get; set; }
}
