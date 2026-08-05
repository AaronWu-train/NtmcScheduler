using Google.OrTools.Sat;

namespace NtmScheduler.Solvers.Common;

/// <summary>
/// Pattern / reified-AND helpers for soft-rule indicators.
/// </summary>
public static class PatternEncoder
{
    public static BoolVar And(CpModel model, string name, IReadOnlyList<BoolVar> literals)
    {
        if (literals.Count == 0)
        {
            var t = model.NewBoolVar(name);
            model.Add(t == 1);
            return t;
        }

        if (literals.Count == 1)
            return literals[0];

        var y = model.NewBoolVar(name);
        foreach (var lit in literals)
            model.AddImplication(y, lit);

        var clause = new List<ILiteral>(literals.Count + 1) { y };
        foreach (var lit in literals)
            clause.Add(lit.Not());
        model.AddBoolOr(clause);
        return y;
    }

    public static BoolVar Or(CpModel model, string name, IReadOnlyList<BoolVar> literals)
    {
        if (literals.Count == 0)
        {
            var f = model.NewBoolVar(name);
            model.Add(f == 0);
            return f;
        }

        if (literals.Count == 1)
            return literals[0];

        var y = model.NewBoolVar(name);
        model.AddBoolOr(literals).OnlyEnforceIf(y);
        foreach (var lit in literals)
            model.AddImplication(lit, y);
        return y;
    }

    public static int Deviation(int length) =>
        Math.Max(0, 3 - length) + Math.Max(0, length - 5);

    /// <summary>BoolVar equal to ¬v.</summary>
    public static BoolVar NotAsBool(CpModel model, BoolVar v, string name)
    {
        var n = model.NewBoolVar(name);
        model.Add(n + v == 1);
        return n;
    }

    /// <summary>1 − lit as LinearExpr.</summary>
    public static LinearExpr OneMinus(BoolVar lit) => 1 - lit;
}
