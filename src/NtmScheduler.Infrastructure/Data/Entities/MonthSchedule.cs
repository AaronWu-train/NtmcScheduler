using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Data.Entities;

/// <summary>Current editable schedule for one (Unit, Month). At most one row per pair.</summary>
public sealed class MonthSchedule
{
    public long Id { get; set; }
    public Unit Unit { get; set; }
    /// <summary>yyyy-MM</summary>
    public string Month { get; set; } = "";
    public long? SourceRunId { get; set; }
    public long? SourceCandidateId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Operator { get; set; } = "";

    public ScheduleRun? SourceRun { get; set; }
    public CandidateSolution? SourceCandidate { get; set; }
    public ICollection<ScheduleEdit> Edits { get; set; } = new List<ScheduleEdit>();
}
