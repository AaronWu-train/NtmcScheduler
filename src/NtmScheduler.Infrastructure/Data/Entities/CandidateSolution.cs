namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class CandidateSolution
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public int Index { get; set; }
    public bool IsShortageAnalysis { get; set; }
    public string MetricsJson { get; set; } = "{}";
    public string? CoverageCsv { get; set; }
    public string? ViolationsCsv { get; set; }

    public ScheduleRun? Run { get; set; }
}
