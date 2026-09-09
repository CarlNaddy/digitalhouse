using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace DigitalHouse.Components;

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

    /// <summary>The signed-in user's id (the <see cref="ClaimTypes.NameIdentifier"/>
    /// claim), or null when anonymous.</summary>
    public static async Task<string?> UserIdAsync(this Task<AuthenticationState>? authState)
    {
        if (authState is null)
        {
            return null;
        }

        var state = await authState;
        return state.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
