namespace NtmScheduler.Core.Domain;

public sealed record XEvent(
    string EmployeeId,
    DateTime Start,
    DateTime End,
    string Description)
{
    public DateOnly StartDate => DateOnly.FromDateTime(Start);
}
