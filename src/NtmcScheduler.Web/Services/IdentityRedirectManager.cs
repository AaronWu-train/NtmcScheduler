using Microsoft.AspNetCore.Components;

namespace NtmcScheduler.Web.Services;

public sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public void RedirectTo(string? uri)
    {
        uri ??= "";
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative)) uri = navigationManager.ToBaseRelativePath(uri);
        navigationManager.NavigateTo(uri, forceLoad: true);
    }
}
