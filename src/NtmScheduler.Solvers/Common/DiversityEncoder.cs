using Google.OrTools.Sat;
using NtmScheduler.Core.Domain;

namespace NtmScheduler.Solvers.Common;

/// <summary>
/// Candidate diversity literals (docs/06): R↔R* same (both rest); R↔R1 different.
/// </summary>
public static class DiversityEncoder
{
    public static int DecisionCellCount(
        IEnumerable<(string EmployeeId, DateOnly Date)> decisionCells) =>
        decisionCells.Count();

    public static int Threshold(int denominator) =>
        (int)Math.Ceiling(denominator * 0.10);

    /// <summary>
    /// For an incumbent assignment, build Σ diffLit ≥ threshold constraint terms.
    /// diffLit = 1 − literal_of_incumbent_state.
    /// </summary>
    public static LinearExpr DifferenceFromIncumbent(
        CpModel model,
        IReadOnlyList<(BoolVar IncumbentLiteral, string Label)> incumbentLiterals)
    {
        if (incumbentLiterals.Count == 0)
            return model.NewConstant(0);

        var diffs = new LinearExpr[incumbentLiterals.Count];
        for (var i = 0; i < incumbentLiterals.Count; i++)
        {
            var (lit, _) = incumbentLiterals[i];
            diffs[i] = PatternEncoder.OneMinus(lit);
        }

        return LinearExpr.Sum(diffs);
    }
}
