using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class Employee
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Unit Unit { get; set; }
    public string? HomeStation { get; set; }
    public string? Specialty { get; set; }
    public int? Ability { get; set; }

    public ICollection<EmployeeMonthlyShift> MonthlyShifts { get; set; } = new List<EmployeeMonthlyShift>();
    public ICollection<FixedEvent> FixedEvents { get; set; } = new List<FixedEvent>();
}
