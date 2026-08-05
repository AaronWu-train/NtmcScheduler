using Microsoft.Extensions.DependencyInjection;
using NtmScheduler.Core.Abstractions;

namespace NtmScheduler.Solvers;

public static class DependencyInjection
{
    public static IServiceCollection AddSolvers(this IServiceCollection services)
    {
        services.AddSingleton<ISolveService, SolveService>();
        return services;
    }
}
