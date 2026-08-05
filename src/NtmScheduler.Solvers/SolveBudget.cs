using System.Diagnostics;

namespace NtmScheduler.Solvers;

public sealed class SolveBudget
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly TimeSpan _total;

    public SolveBudget(TimeSpan totalTimeLimit) => _total = totalTimeLimit;

    public TimeSpan Remaining
    {
        get
        {
            var left = _total - _sw.Elapsed;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    public double RemainingSeconds => Math.Max(0.001, Remaining.TotalSeconds);

    public bool Exhausted => Remaining <= TimeSpan.Zero;
}
