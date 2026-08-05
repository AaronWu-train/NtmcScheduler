using NtmScheduler.Core.Domain;
using NtmScheduler.Core.Evaluation.Rules.Hard;
using NtmScheduler.Core.Evaluation.Rules.Soft;

namespace NtmScheduler.Core.Evaluation;

/// <summary>
/// Authoritative pure-C# rule evaluation used by Draft/Publish and solver cross-checks.
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

    public static IReadOnlyList<SoftRuleSpecDefaults> DefaultSoftRules(Unit unit) =>
        unit == Unit.M
            ?
            [
                new("GEN-R-01", 1, true),
                new("M-S-EXT", 2, true),
                new("M-S-HOME", 3, true),
                new("GEN-S-STREAK", 4, true),
                new("M-S-BLOCK", 5, true),
                new("M-S-NIGHT-EARLY", 6, true),
                new("M-S-NIGHT-AFTERNOON", 7, true),
                new("M-S-RESTSWITCH", 8, true),
                new("M-S-ROTATE", 9, true),
                new("GEN-S-WEEKDAY-R", 10, true),
                new("GEN-S-WEEKEND-R", 11, true),
                new("M-S-SUPPORT-FAIR", 12, true),
            ]
            :
            [
                new("GEN-R-01", 1, true),
                new("T-S-ATTEND", 2, true),
                new("T-S-SPECIALTY", 3, true),
                new("T-S-ABILITY", 4, true),
                new("GEN-S-STREAK", 5, true),
                new("T-S-MONTH-REST", 6, true),
                new("T-S-MONTH-BALANCE", 7, true),
                new("GEN-S-WEEKDAY-R", 8, true),
                new("GEN-S-WEEKEND-R", 9, true),
            ];
}

public sealed record SoftRuleSpecDefaults(string RuleId, int Order, bool Enabled);
