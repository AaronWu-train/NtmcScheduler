using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Web.Services;

public sealed class CurrentActorService(
    AuthenticationStateProvider authenticationStateProvider,
    IDbContextFactory<NtmcDbContext> dbFactory,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<ActorContext> GetAsync(CancellationToken cancellationToken = default)
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var idText = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idText, out var userId) || principal.Identity?.IsAuthenticated != true)
            throw new ForbiddenOperationException("請先登入。");
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var workspaces = await db.WorkspacePermissions.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Workspace).ToListAsync(cancellationToken);
        var context = httpContextAccessor.HttpContext;
        return new(
            userId,
            principal.Identity!.Name ?? "unknown",
            principal.IsInRole("Administrator"),
            workspaces.ToHashSet(),
            context?.TraceIdentifier ?? Guid.NewGuid().ToString("N"),
            ActorContextFactory.ParseSessionId(principal),
            context?.Connection.RemoteIpAddress?.ToString(),
            context?.Request.Headers.UserAgent.ToString(),
            principal.FindFirstValue("must_change_password") == "true");
    }
}
