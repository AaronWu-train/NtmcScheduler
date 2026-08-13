using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NtmScheduler.Contracts;
using NtmScheduler.Infrastructure.Background;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Services;

namespace NtmScheduler.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNtmInfrastructure(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.AddDbContext<NtmDbContext>(configureDatabase);
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<ICommonConfigurationService, CommonConfigurationService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IDemandService, DemandService>();
        services.AddScoped<IScheduleRunService, ScheduleRunService>();
        services.AddScoped<IScheduleValidationService, ScheduleValidationService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddSingleton<ScheduleRunQueue>();
        services.AddHostedService<ScheduleRunWorker>();
        return services;
    }
}
