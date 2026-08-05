namespace NtmScheduler.Core.Domain;

public sealed record EmployeeHistory(
    IReadOnlyDictionary<DateOnly, DayState> Days,
    DateTime? LastWorkEnd,
    (ShiftType Shift, int Count)? OpenBlock);
