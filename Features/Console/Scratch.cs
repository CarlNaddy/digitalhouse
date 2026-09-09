using DigitalHouse.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Features.Console;

/// <summary>
/// The <c>rails console</c> substitute (parity plan P6.2). Edit this method,
/// then run <c>dotnet run -- console</c> — it runs against the real,
/// configured app (user-secrets connection string, every registered
/// service), not a test fixture. Full compile-time checking, no scripting
/// engine: this is exactly <see cref="Seed.SeedCommand"/>'s pattern
/// generalized to "whatever one-off task you need right now."
/// </summary>
/// <remarks>
/// Convention: after you're done, <c>git checkout -- Features/Console/Scratch.cs</c>
/// to restore this starter body — treat it like shell history, not like
/// <c>db/seeds.rb</c>. If a snippet turns out to be worth keeping, promote it
/// to a real job (<c>Features/Jobs/</c>) or a proper CLI verb instead of
/// leaving it here.
/// </remarks>
public static class Scratch
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);

        var userCount = await db.Users.CountAsync(ct);
        System.Console.WriteLine($"Users: {userCount}");
    }
}
