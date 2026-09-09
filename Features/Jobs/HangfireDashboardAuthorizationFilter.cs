using Hangfire.Dashboard;

namespace DigitalHouse.Features.Jobs;

/// <summary>
/// Gates the Hangfire dashboard (<c>/hangfire</c>) to the <c>Admin</c> role —
/// the dashboard shows job payloads and lets you trigger/delete jobs, so it's
/// not public. Same role the <c>ListingsAdmin</c> policy uses (parity plan
/// P3.5); the dev admin is seeded by <c>IdentitySeeder</c> (P3.6).
/// </summary>
public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole("Admin");
    }
}
