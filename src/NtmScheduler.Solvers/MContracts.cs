namespace NtmScheduler.Solvers;

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
