namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class ScheduleEdit
{
    public long Id { get; set; }
    public long ScheduleId { get; set; }
    public int Seq { get; set; }
    public string EmployeeId { get; set; } = "";
    public DateOnly Date { get; set; }
    /// <summary>Display state before edit (preserves R1).</summary>
    public string BeforeState { get; set; } = "";
    /// <summary>Display state after edit (preserves R1).</summary>
    public string AfterState { get; set; } = "";
    public string Operator { get; set; } = "";
    public DateTime At { get; set; }

    public MonthSchedule? Schedule { get; set; }
}
