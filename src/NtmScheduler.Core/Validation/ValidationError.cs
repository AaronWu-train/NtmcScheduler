namespace NtmScheduler.Core.Validation;

public sealed record ValidationError(
    string Code,
    string Message,
    string? EmployeeId = null,
    DateOnly? Date = null);
