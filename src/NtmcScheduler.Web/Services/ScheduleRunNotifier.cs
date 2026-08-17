using NtmcScheduler.Contracts;

namespace NtmcScheduler.Web.Services;

public sealed class ScheduleRunNotifier : IScheduleRunNotifier
{
    public event Action<ScheduleRunDto>? Changed;

    public Task NotifyAsync(ScheduleRunDto run, CancellationToken cancellationToken = default)
    {
        Changed?.Invoke(run);
        return Task.CompletedTask;
    }
}
