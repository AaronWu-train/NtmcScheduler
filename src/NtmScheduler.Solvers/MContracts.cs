namespace NtmScheduler.Solvers;

/// <summary>Optional 56-day M schedule patterns used only as CP-SAT solution hints.</summary>
public sealed record MPerpetualSchedule(
    IReadOnlyDictionary<string, IReadOnlyList<ScheduleCell?>> Patterns);

public sealed record MExternalAssignment(
    DateOnly Date,
    string Station,
    Shift Shift,
    int Count);

public sealed record MCandidate(
    MonthlySchedule Schedule,
    IReadOnlyList<MExternalAssignment> ExternalAssignments,
    IReadOnlyList<ObjectiveScore> Objectives);

public sealed record MSolveResult(
    SolveStatus Status,
    IReadOnlyList<MCandidate> Candidates,
    IReadOnlyList<InputError> Errors);
