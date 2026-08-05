using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class OfficialScheduleVersion
{
    public long Id { get; set; }
    public Unit Unit { get; set; }
    /// <summary>yyyy-MM</summary>
    public string Month { get; set; } = "";
    public int VersionNo { get; set; }
    public DateTime PublishedAt { get; set; }
    public string Operator { get; set; } = "";
    public bool IsCurrent { get; set; }
}
