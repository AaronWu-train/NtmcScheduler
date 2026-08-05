namespace NtmScheduler.Core.Abstractions.Dtos;

/// <summary>M coverage.csv export row (display strings).</summary>
public sealed record MCoverageCsvRow(
    DateOnly Date,
    string Location,
    string Shift,
    int Required,
    int Assigned,
    int External,
    int Unassigned);

/// <summary>T t_coverage.csv export row (display strings).</summary>
public sealed record TCoverageCsvRow(
    DateOnly Date,
    string Shift,
    int GroupSize,
    int NormalAttend,
    int AttendTarget,
    decimal AvgAbility,
    IReadOnlyList<string> MissingSpecialties);

public sealed record ViolationCsvRow(
    string SolutionId,
    string RuleId,
    DateOnly? Date,
    string? EmployeeId,
    string Message);
