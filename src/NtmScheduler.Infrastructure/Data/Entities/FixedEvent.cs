using NtmScheduler.Core.Abstractions.Dtos;

namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class FixedEvent
{
    public long Id { get; set; }
    public string EmployeeId { get; set; } = "";
    public FixedEventType Type { get; set; }
    public DateOnly? Date { get; set; }
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public string? Description { get; set; }

    public Employee? Employee { get; set; }
}
