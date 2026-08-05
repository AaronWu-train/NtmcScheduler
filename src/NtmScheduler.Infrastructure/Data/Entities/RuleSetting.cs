using NtmScheduler.Core.Domain;

namespace NtmScheduler.Infrastructure.Data.Entities;

public sealed class RuleSetting
{
    public long Id { get; set; }
    public Unit Unit { get; set; }
    public string RuleId { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; }
    public int Order { get; set; }
    public string? ParametersJson { get; set; }
}
