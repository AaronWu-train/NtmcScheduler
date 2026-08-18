using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Infrastructure.Services;

public static class AuditWriter
{
    public static void Add(
        NtmcDbContext db,
        ActorContext actor,
        string action,
        WorkspaceCode? workspace,
        string resourceType,
        object resourceId,
        object? before,
        object? after,
        bool succeeded = true) =>
        ServiceSupport.AddAudit(db, actor, action, workspace, resourceType, resourceId, before, after, succeeded);
}
