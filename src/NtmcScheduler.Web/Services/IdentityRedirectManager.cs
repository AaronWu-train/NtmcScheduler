using Microsoft.AspNetCore.Components;

namespace NtmcScheduler.Web.Services;

public sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public void RedirectTo(string? uri) => navigationManager.NavigateTo(Resolve(uri), forceLoad: true);

    // Resolving against the base URI keeps a caller-supplied return URL inside the application:
    // "//host", "https://host" and non-http schemes all resolve outside the base and fall back
    // to the home page. Navigating to the resolved absolute URI also removes the ambiguity of
    // forms such as "/\host" that browsers may treat as protocol-relative.
    private string Resolve(string? uri)
    {
        var baseUri = new Uri(navigationManager.BaseUri);
        return Uri.TryCreate(baseUri, uri, out var target) && baseUri.IsBaseOf(target)
            ? target.AbsoluteUri
            : navigationManager.BaseUri;
    }
}
