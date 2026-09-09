using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace DotnetAgenticStarterkit.Components.Account;

/// <summary>
/// Redirect helper for Identity's static-SSR pages (Register, Login). During
/// static rendering, <c>NavigationManager.NavigateTo</c> throws a
/// <c>NavigationException</c> that the framework turns into an HTTP redirect —
/// the Post/Redirect/Get pattern these <c>EditForm</c> submissions need. See
/// <c>dotnet-blazor:configure-auth</c>.
/// </summary>
internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "";

        // Prevent open redirects.
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }

        navigationManager.NavigateTo(uri);
        throw new InvalidOperationException(
            $"{nameof(IdentityRedirectManager)} can only be used during static rendering.");
    }
}
