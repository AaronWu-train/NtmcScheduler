using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Background;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.Services;
using NtmScheduler.Solvers;

namespace NtmScheduler.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Alias required by M3 plan: AddInfrastructure(services, connectionString).</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString) =>
        AddNtmInfrastructure(services, connectionString);

    public static IServiceCollection AddNtmInfrastructure(
        this IServiceCollection services,
        string sqliteConnectionString)
    {
        services.AddDbContext<NtmDbContext>(opt =>
            opt.UseSqlite(sqliteConnectionString));

        services.AddScoped<AuditWriter>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IMonthlyShiftService, MonthlyShiftService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IScheduleCycleService, ScheduleCycleService>();
        services.AddScoped<IRuleSettingService, RuleSettingService>();
        services.AddScoped<IScheduleRunService, ScheduleRunService>();
        services.AddScoped<ICandidateService, CandidateService>();
        services.AddScoped<DraftService>();
        services.AddScoped<IDraftService>(sp => sp.GetRequiredService<DraftService>());
        services.AddScoped<IPublishService, PublishService>();
        services.AddScoped<IHistoryImportService, HistoryImportService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IShortageAnalysisService, ShortageAnalysisService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IPreparationService, PreparationService>();

        services.AddSolvers();
        services.AddHostedService<ScheduleRunWorker>();

        return services;
    }
}
