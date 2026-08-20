using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Infrastructure.Services;

public sealed class AuditQueryService(IDbContextFactory<NtmcDbContext> dbFactory) : IAuditQueryService
{
    public async Task<IReadOnlyList<AuditLogDto>> QueryAsync(
        DateOnly? from,
        DateOnly? to,
        WorkspaceCode? workspace,
        string? action,
        string? actorName,
        Guid? sessionId,
        string? ipAddress,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ServiceSupport.RequireAdministrator(actor);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
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
        if (!string.IsNullOrWhiteSpace(actorName)) query = query.Where(x => x.ActorName.Contains(actorName));
        if (sessionId is not null) query = query.Where(x => x.SessionId == sessionId);
        if (!string.IsNullOrWhiteSpace(ipAddress)) query = query.Where(x => x.IpAddress == ipAddress);
        var rows = await query.OrderByDescending(x => x.AtUtcTicks).Take(1000).ToListAsync(cancellationToken);
        var assignmentContexts = await LoadAssignmentContextsAsync(db, rows, cancellationToken);
        return rows.Select(row => AuditPresentation.Format(row, assignmentContexts)).ToArray();
    }

    private static async Task<IReadOnlyDictionary<Guid, AuditAssignmentContext>> LoadAssignmentContextsAsync(
        NtmcDbContext db,
        IReadOnlyList<AuditLog> rows,
        CancellationToken cancellationToken)
    {
        var ids = rows.Where(x => x.ResourceType == "ScheduleAssignment")
            .Select(x => Guid.TryParse(x.ResourceId, out var id) ? id : (Guid?)null)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, AuditAssignmentContext>();

        var assignments = await db.ScheduleAssignments.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Employee.EmployeeCode,
                EmployeeName = x.Employee.Name,
                x.Date,
                x.Employee.ScheduleVersion.Month,
                ScheduleName = x.Employee.ScheduleVersion.Name
            })
            .ToListAsync(cancellationToken);

        return assignments.ToDictionary(
            x => x.Id,
            x => new AuditAssignmentContext(x.EmployeeCode, x.EmployeeName, x.Date, x.Month, x.ScheduleName));
    }
}
