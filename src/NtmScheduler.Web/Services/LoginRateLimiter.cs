using System.Collections.Concurrent;

namespace NtmScheduler.Web.Services;

public sealed class LoginRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int MaximumAttempts = 10;
    private readonly ConcurrentDictionary<string, AttemptWindow> attempts = new(StringComparer.Ordinal);

    public bool IsAllowed(string userName, string? ipAddress, DateTimeOffset now) =>
        BelowLimit(AccountKey(userName), now) && BelowLimit(IpKey(ipAddress), now);

    public void RecordFailure(string userName, string? ipAddress, DateTimeOffset now)
    {
        Increment(AccountKey(userName), now);
        Increment(IpKey(ipAddress), now);
    }

    public void ResetAccount(string userName) => attempts.TryRemove(AccountKey(userName), out _);

    private bool BelowLimit(string key, DateTimeOffset now)
    {
        if (!attempts.TryGetValue(key, out var current) || now - current.Start >= Window) return true;
        return current.Count < MaximumAttempts;
    }

    private void Increment(string key, DateTimeOffset now)
    {
        while (true)
        {
            var current = attempts.GetOrAdd(key, _ => new(now, 0));
            var next = now - current.Start >= Window ? new AttemptWindow(now, 1) : current with { Count = current.Count + 1 };
            if (attempts.TryUpdate(key, next, current)) return;
        }
    }

    private static string AccountKey(string userName) => $"account|{userName.Trim().ToUpperInvariant()}";
    private static string IpKey(string? ipAddress) => $"ip|{ipAddress ?? "unknown"}";
    private sealed record AttemptWindow(DateTimeOffset Start, int Count);
}
