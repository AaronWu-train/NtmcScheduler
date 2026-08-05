namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class DraftSchedule
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public long SourceCandidateId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Operator { get; set; } = "";

    public ScheduleRun? Run { get; set; }
    public CandidateSolution? SourceCandidate { get; set; }
    public ICollection<DraftEdit> Edits { get; set; } = new List<DraftEdit>();
}
