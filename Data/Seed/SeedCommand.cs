using DigitalHouse.Features.Files;
using DigitalHouse.Features.Marketplace;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Data.Seed;

/// <summary>
/// Entry point for <c>dotnet run -- seed</c>: applies pending migrations, then
/// runs <see cref="DbSeeder"/> (sample data) and <see cref="IdentitySeeder"/>
/// (Admin role + dev admin user). Keeps a fresh clone to a single command.
/// </summary>
public static class SeedCommand
{
    public const string Verb = "seed";

    public static async Task RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(Verb);

        await using var db = await factory.CreateDbContextAsync();

        logger.LogInformation("Applying pending migrations...");
        await db.Database.MigrateAsync();

        await DbSeeder.SeedAsync(db, logger);

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = sp.GetRequiredService<IConfiguration>();

        await IdentitySeeder.SeedAsync(
            userManager,
            sp.GetRequiredService<RoleManager<IdentityRole>>(),
            configuration,
            sp.GetRequiredService<IHostEnvironment>().IsDevelopment(),
            logger);

        var adminEmail = configuration["Seed:AdminEmail"] ?? "admin@DigitalHouse.local";
        var demoOwner = await userManager.FindByEmailAsync(adminEmail);

        await MarketplaceSeeder.SeedAsync(
            db,
            sp.GetRequiredService<PricingEngine>(),
            sp.GetRequiredService<IFileStore>(),
            sp.GetRequiredService<TimeProvider>(),
            demoOwner?.Id,
            logger);
    }
}
