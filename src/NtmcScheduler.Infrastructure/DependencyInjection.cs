using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Background;
using NtmcScheduler.Infrastructure.Data;
using NtmcScheduler.Infrastructure.Services;

namespace NtmcScheduler.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNtmcInfrastructure(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.AddDbContext<NtmcDbContext>(configureDatabase);
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
