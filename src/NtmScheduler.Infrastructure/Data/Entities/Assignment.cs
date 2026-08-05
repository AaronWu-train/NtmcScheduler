using NtmScheduler.Core.Abstractions;

namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class Assignment
{
    public long Id { get; set; }
    public AssignmentOwnerType OwnerType { get; set; }
    public long OwnerId { get; set; }
    public string EmployeeId { get; set; } = "";
    public DateOnly Date { get; set; }
    /// <summary>
    /// Display value: 早/午/夜/R/R*/R1/X/LB03-早 (must preserve R1 as R1).
    /// </summary>
    public string State { get; set; } = "";
}
