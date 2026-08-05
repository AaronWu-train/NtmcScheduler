namespace NtmScheduler.Core.Abstractions.Dtos;

/// <summary>Per-employee rest statistics shown in coverage / draft panels.</summary>
public sealed record RestStatsDto(
    string EmployeeId,
    int MonthGeneralRest,
    int MonthR1,
    int CycleGeneralRest,
    int CycleR1,
    int RequiredR,
    int RequiredR1,
    int ReservedGeneralRest)
{
    /// <summary>Alias used by older call sites.</summary>
    public int MonthR => MonthGeneralRest;

    /// <summary>Alias used by older call sites.</summary>
    public int CycleR => CycleGeneralRest;
}
