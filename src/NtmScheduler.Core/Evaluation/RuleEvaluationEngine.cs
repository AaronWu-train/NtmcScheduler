using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation.Rules.Hard;
using NtmScheduler.Core.Evaluation.Rules.Soft;

namespace NtmScheduler.Core.Evaluation;

/// <summary>
/// Authoritative pure-C# rule evaluation used by schedule editing and solver cross-checks.
/// Soft-rule defaults come from <see cref="RuleCatalog"/> (single source of truth).
/// </summary>
public sealed class RuleEvaluationEngine
{
    private readonly IReadOnlyList<IRuleEvaluator> _evaluators;

    public RuleEvaluationEngine(IEnumerable<IRuleEvaluator>? evaluators = null)
    {
        _evaluators = (evaluators ?? CreateDefaultEvaluators()).ToList();
    }

    public static IReadOnlyList<IRuleEvaluator> CreateDefaultEvaluators() =>
    [
        new GenH01DailyUnique(),
        new GenH02ContinuousWork(),
        new GenH03RestGap(),
        new GenH04CycleRest(),
        new GenH05FixedHistory(),
        new MH01GroupConstraint(),
        new MH02Coverage(),
        new MH03ExternalStations(),
        new TH01FixedShift(),
        new GenR01(),
        new MsExt(),
        new MsHome(),
        new TsAttend(),
        new TsSpecialty(),
        new TsAbility(),
        new GenSStreak(),
        new MsBlock(),
        new MsNightEarly(),
        new MsNightAfternoon(),
        new MsRestSwitch(),
        new MsRotate(),
        new TsMonthRest(),
        new TsMonthBalance(),
        new GenSWeekdayR(),
        new GenSWeekendR(),
        new MsSupportFair(),
    ];

    public IReadOnlyList<RuleResult> EvaluateAll(ScheduleContext ctx) =>
        _evaluators.Select(e => e.Evaluate(ctx)).ToList();

    public IReadOnlyDictionary<string, int> EvaluateMetrics(ScheduleContext ctx) =>
        _evaluators.ToDictionary(e => e.RuleId, e => e.Evaluate(ctx).ViolationCount);

    public IReadOnlyList<RuleResult> EvaluateHard(ScheduleContext ctx) =>
        _evaluators.Where(e => e.RuleId.Contains("-H-", StringComparison.Ordinal))
            .Select(e => e.Evaluate(ctx)).ToList();

    public bool AllHardPassed(ScheduleContext ctx) =>
        EvaluateHard(ctx).All(r => r.ViolationCount == 0);

    public RuleResult Evaluate(string ruleId, ScheduleContext ctx)
    {
        var ev = _evaluators.FirstOrDefault(e => e.RuleId == ruleId)
            ?? throw new ArgumentException($"未知規則：{ruleId}", nameof(ruleId));
        return ev.Evaluate(ctx);
    }

    /// <summary>軟規則預設順序（單一來源：RuleCatalog）。</summary>
    public static IReadOnlyList<SoftRuleSpecDefaults> DefaultSoftRules(Unit unit) =>
        RuleCatalog.DefaultSoftOrder(unit)
            .Select(x => new SoftRuleSpecDefaults(x.RuleId, x.Order, true))
            .ToList();
}

public sealed record SoftRuleSpecDefaults(string RuleId, int Order, bool Enabled);
