using Microsoft.AspNetCore.Localization;

namespace DotnetAgenticStarterkit.Endpoints;

public static class CultureEndpoints
{
    /// <summary>
    /// <c>GET /culture/set?culture=de&amp;redirectUri=/listings</c> — writes the
    /// culture cookie and bounces back. The full-page redirect is what lets a new
    /// Blazor Server circuit pick up the culture.
    /// </summary>
    public static IEndpointRouteBuilder MapCultureEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/culture/set", (string culture, string redirectUri, HttpContext http) =>
        {
            http.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    Path = "/",
                });

            return Results.LocalRedirect(redirectUri);
        });

        return app;
    }
}
