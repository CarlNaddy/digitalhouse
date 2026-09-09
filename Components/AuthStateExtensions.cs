using Microsoft.AspNetCore.Components.Authorization;

namespace DotnetAgenticStarterkit.Components;

/// <summary>
/// Small helpers for reading the cascading <see cref="AuthenticationState"/> from
/// interactive component code — the defence-in-depth check behind an
/// <c>AuthorizeView</c> that only hides UI (parity plan P3.5).
/// </summary>
internal static class AuthStateExtensions
{
    public static async Task<bool> IsInRoleAsync(this Task<AuthenticationState>? authState, string role)
    {
        if (authState is null)
        {
            return false;
        }

        var state = await authState;
        return state.User.IsInRole(role);
    }
}
