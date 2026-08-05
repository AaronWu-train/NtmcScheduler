using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class EmployeeMonthlyShift
{
    public string EmployeeId { get; set; } = "";
    /// <summary>yyyy-MM</summary>
    public string Month { get; set; } = "";
    public ShiftType Shift { get; set; }

    public Employee? Employee { get; set; }
}
