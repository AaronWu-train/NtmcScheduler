using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NtmScheduler.Core.Abstractions;
using NtmScheduler.Infrastructure.Auditing;
using NtmScheduler.Infrastructure.Background;
using NtmScheduler.Infrastructure.Data;
using NtmScheduler.Infrastructure.SampleData;
using NtmScheduler.Infrastructure.Services;
using NtmScheduler.Solvers;

namespace NtmScheduler.Infrastructure;

public static class DependencyInjection
{
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
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IHistoryImportService, HistoryImportService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IShortageAnalysisService, ShortageAnalysisService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IPreparationService, PreparationService>();
        services.AddScoped<IDemoDataService, DemoDataSeeder>();

        services.AddSingleton<ISolveService, SolveService>();
        services.AddHostedService<ScheduleRunWorker>();

        return services;
    }
}
