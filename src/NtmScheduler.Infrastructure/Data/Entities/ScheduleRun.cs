using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Data.Entities;

public enum ScheduleRunStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

public enum ScheduleStatusCode
{
    Feasible,
    Infeasible,
    InvalidInput
}

public enum OptimizationStatusCode
{
    Optimal,
    TimeLimit
}

public sealed class ScheduleRun
{
    public long Id { get; set; }
    public Unit Unit { get; set; }
    /// <summary>yyyy-MM</summary>
    public string TargetMonth { get; set; } = "";
    public ScheduleRunStatus Status { get; set; }
    public ScheduleStatusCode? ScheduleStatus { get; set; }
    public OptimizationStatusCode? OptimizationStatus { get; set; }
    public int Seed { get; set; }
    public string ProgramVersion { get; set; } = "";
    public string Operator { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string? ProgressJson { get; set; }
    public string? ResultJson { get; set; }
    public int CandidateCount { get; set; }
    public bool ShortageAnalysisAvailable { get; set; }

    public ICollection<CandidateSolution> Candidates { get; set; } = new List<CandidateSolution>();
    public ICollection<DraftSchedule> Drafts { get; set; } = new List<DraftSchedule>();
}
