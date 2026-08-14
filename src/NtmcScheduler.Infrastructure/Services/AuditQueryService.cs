using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class AuditQueryService(NtmcDbContext db) : IAuditQueryService
{
    public async Task<IReadOnlyList<AuditLogDto>> QueryAsync(DateOnly? from, DateOnly? to, WorkspaceCode? workspace, string? action, ActorContext actor, CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireAdministrator(actor);
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (from is not null)
        {
            var fromTicks = new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).UtcTicks;
            query = query.Where(x => x.AtUtcTicks >= fromTicks);
        }
        if (to is not null)
        {
            var toTicks = new DateTimeOffset(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).UtcTicks;
            query = query.Where(x => x.AtUtcTicks < toTicks);
        }
        if (workspace is not null) query = query.Where(x => x.Workspace == workspace);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action);
        return await query.OrderByDescending(x => x.AtUtcTicks).Take(1000)
            .Select(x => new AuditLogDto(x.Id, x.AtUtc, x.ActorName, x.Action, x.Workspace, x.ResourceType, x.ResourceId, x.Succeeded, x.CorrelationId))
            .ToListAsync(cancellationToken);
    }
}
