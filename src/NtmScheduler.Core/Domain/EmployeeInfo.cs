namespace NtmScheduler.Core.Domain;

public sealed record EmployeeInfo(
    string Id,
    string Name,
    Unit Unit,
    string? HomeStation = null,
    string? Specialty = null,
    int? Ability = null);
