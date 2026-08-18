using System.Security.Claims;
using NtmcScheduler.Contracts;

namespace NtmcScheduler.Web.Services;

internal static class ActorContextFactory
{
    public static Guid NewSessionId() => Guid.NewGuid();

    public static Guid? ParseSessionId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(AuditClaimTypes.SessionId), out var id) ? id : null;

    public static ActorContext FromHttp(HttpContext context, ClaimsPrincipal principal, IReadOnlySet<WorkspaceCode>? workspaces = null, Guid? sessionId = null)
    {
        var userId = Guid.Empty;
        if (principal.Identity?.IsAuthenticated == true &&
            Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed))
            userId = parsed;

        return new(
            userId,
            principal.Identity?.Name ?? "unknown",
            principal.IsInRole("Administrator"),
            workspaces ?? new HashSet<WorkspaceCode>(),
            context.TraceIdentifier,
            sessionId ?? ParseSessionId(principal),
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString(),
            principal.FindFirstValue("must_change_password") == "true");
    }
}
