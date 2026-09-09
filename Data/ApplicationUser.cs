using Microsoft.AspNetCore.Identity;

namespace DigitalHouse.Data;

/// <summary>
/// The application user — the ASP.NET Core Identity user for this app
/// (parity plan P3). Add profile columns here directly as features need them;
/// no separate profile table until one is actually warranted.
/// </summary>
public class ApplicationUser : IdentityUser
{
}
