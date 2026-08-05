namespace NtmScheduler.Core.Abstractions;

public interface IDemoDataService
{
    /// <summary>
    /// Clears operational demo tables and seeds a full workable dataset
    /// (employees, cycles, rules, R*/X, history) for target month 2026-08.
    /// </summary>
    Task SeedAsync(string? operatorName = null, CancellationToken ct = default);
}
