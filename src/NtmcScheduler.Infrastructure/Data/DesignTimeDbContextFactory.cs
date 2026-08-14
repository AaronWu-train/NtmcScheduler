using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NtmcScheduler.Infrastructure.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NtmcDbContext>
{
    public NtmcDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<NtmcDbContext>();
        if (string.Equals(Environment.GetEnvironmentVariable("NTMC_MIGRATION_PROVIDER"), "SqlServer", StringComparison.OrdinalIgnoreCase))
            builder.UseSqlServer("Server=localhost;Database=NtmcSchedulerDesign;User Id=sa;Password=NotUsedForScriptGeneration!1;TrustServerCertificate=True");
        else
            builder.UseSqlite("Data Source=ntmc-scheduler-design.db");
        var options = builder.Options;
        return new NtmcDbContext(options);
    }
}
