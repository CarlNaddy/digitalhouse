using Microsoft.AspNetCore.Identity;

namespace DotnetAgenticStarterkit.Data.Seed;

/// <summary>
/// Seeds the <c>Admin</c> role and a development admin user (parity plan P3.6).
/// Idempotent. Credentials come from configuration keys <c>Seed:AdminEmail</c> /
/// <c>Seed:AdminPassword</c>; outside Development a password must be supplied,
/// in Development a well-known default is used.
/// </summary>
public static class IdentitySeeder
{
    public const string AdminRole = "Admin";

    private const string DefaultAdminEmail = "admin@DotnetAgenticStarterkit.local";
    private const string DefaultAdminPassword = "Admin!23456";

    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration,
        bool isDevelopment,
        ILogger logger)
    {
        if (!await roleManager.RoleExistsAsync(AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(AdminRole));
            logger.LogInformation("Created the {Role} role.", AdminRole);
        }

        var email = configuration["Seed:AdminEmail"] ?? DefaultAdminEmail;

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (await userManager.IsInRoleAsync(existing, AdminRole))
            {
                logger.LogInformation("Admin user {Email} already present.", email);
            }
            else
            {
                await userManager.AddToRoleAsync(existing, AdminRole);
                logger.LogInformation("Added existing user {Email} to the {Role} role.", email, AdminRole);
            }

            return;
        }

        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrEmpty(password))
        {
            if (!isDevelopment)
            {
                throw new InvalidOperationException(
                    "Seed:AdminPassword must be set outside Development — refusing to "
                    + "seed the admin user with the built-in default password.");
            }

            password = DefaultAdminPassword;
            logger.LogWarning(
                "Seeding admin user {Email} with the built-in dev default password.", email);
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create the admin user: {errors}");
        }

        await userManager.AddToRoleAsync(admin, AdminRole);
        logger.LogInformation("Seeded admin user {Email} in the {Role} role.", email, AdminRole);
    }
}
