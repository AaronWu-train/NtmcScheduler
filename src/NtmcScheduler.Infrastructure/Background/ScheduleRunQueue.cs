using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NtmcScheduler.Infrastructure.Background;

public sealed class ScheduleRunQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    // A run owns a cancellation source from the moment it is queued until the worker gives it a
    // final status, so a queued run that has not started yet can be cancelled the same way as a
    // running one. Cancellation is in-process only; a restart re-queues pending runs instead.
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> cancellations = new();

    public async ValueTask QueueAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var source = new CancellationTokenSource();
        if (!cancellations.TryAdd(runId, source)) source.Dispose();
        await channel.Writer.WriteAsync(runId, cancellationToken);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);

    public CancellationToken CancellationFor(Guid runId) =>
        cancellations.TryGetValue(runId, out var source) ? source.Token : CancellationToken.None;

    /// <summary>Requests cancellation; false when the run already reached a final status.</summary>
    public bool Cancel(Guid runId)
    {
        if (!cancellations.TryGetValue(runId, out var source)) return false;
        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Release(Guid runId)
    {
        if (cancellations.TryRemove(runId, out var source)) source.Dispose();
    }
}
