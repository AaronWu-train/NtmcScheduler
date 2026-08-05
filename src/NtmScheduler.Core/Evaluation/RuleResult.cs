namespace NtmScheduler.Core.Evaluation;

public sealed record ViolationItem(
    string RuleId,
    string? EmployeeId,
    DateOnly? Date,
    string Message);

public sealed record RuleResult(
    string RuleId,
    int ViolationCount,
    IReadOnlyList<ViolationItem> Items)
{
    public static RuleResult Ok(string ruleId) =>
        new(ruleId, 0, Array.Empty<ViolationItem>());

    public static RuleResult From(string ruleId, IReadOnlyList<ViolationItem> items) =>
        new(ruleId, items.Count, items);
}
