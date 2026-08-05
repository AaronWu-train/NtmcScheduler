using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NtmScheduler.Infrastructure.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NtmDbContext>
{
    public NtmDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NtmDbContext>()
            .UseSqlite("Data Source=ntm_scheduler_design.db")
            .Options;
        return new NtmDbContext(options);
    }
}
