namespace NtmScheduler.Core.Abstractions.Dtos;

public enum FixedEventType
{
    RStar,
    X
}

public sealed record FixedEventDto(
    long Id,
    string EmployeeId,
    FixedEventType Type,
    DateOnly? Date,
    DateTime? Start,
    DateTime? End,
    string? Description);
