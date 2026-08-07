namespace NtmScheduler.Solvers;

public sealed record TCandidate(
    MonthlySchedule Schedule,
    IReadOnlyList<ObjectiveScore> Objectives);

public sealed record TSolveResult(
    SolveStatus Status,
    IReadOnlyList<TCandidate> Candidates,
    IReadOnlyList<InputError> Errors);
