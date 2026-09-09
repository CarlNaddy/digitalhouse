using DotnetAgenticStarterkit.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DotnetAgenticStarterkit.Endpoints;

public static class AccountEndpoints
{
    /// <summary>
    /// Account actions that must run as real HTTP requests rather than inside a
    /// Razor component — sign-out and the external-login OAuth challenge both
    /// need to touch the response before it starts
    /// (<c>dotnet-blazor:configure-auth</c>).
    /// </summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/Account");

        // POST /Account/Logout — sign out, then redirect.
        group.MapPost("/Logout", async (
            SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
        });

        // POST /Account/PerformExternalLogin — start the OAuth dance for one
        // provider (P3.4). The handler bounces the user to the provider; the
        // provider's callback (/signin-<provider>) then lands on
        // /Account/ExternalLogin, which links or provisions the local user.
        group.MapPost("/PerformExternalLogin", (
            SignInManager<ApplicationUser> signInManager,
            [FromForm] string provider,
            [FromForm] string? returnUrl) =>
        {
            var callback =
                $"/Account/ExternalLogin?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";
            var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, callback);
            return Results.Challenge(properties, [provider]);
        });

        return app;
    }
}
