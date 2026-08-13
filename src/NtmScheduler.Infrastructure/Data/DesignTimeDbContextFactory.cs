using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NtmScheduler.Infrastructure.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NtmDbContext>
{
    public NtmDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<NtmDbContext>();
        if (string.Equals(Environment.GetEnvironmentVariable("NTM_MIGRATION_PROVIDER"), "SqlServer", StringComparison.OrdinalIgnoreCase))
            builder.UseSqlServer("Server=localhost;Database=NtmSchedulerDesign;User Id=sa;Password=NotUsedForScriptGeneration!1;TrustServerCertificate=True");
        else
            builder.UseSqlite("Data Source=ntm-scheduler-design.db");
        var options = builder.Options;
        return new NtmDbContext(options);
    }
}
