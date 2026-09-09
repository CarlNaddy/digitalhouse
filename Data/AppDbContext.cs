using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DigitalHouse.Data;

/// <summary>
/// The application's Entity Framework Core context. Also the ASP.NET Core
/// Identity store (parity plan P3.2) and the Data Protection key store
/// (<see cref="IDataProtectionKeyContext"/>, P5.4 — keys persist in the same
/// Postgres instance instead of regenerating on every restart, the same "no
/// separate infra" pattern as everything else in this app) — one database,
/// one context, one migration history. Entities are added to it as features
/// land.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    // IDataProtectionKeyContext requires a settable property, not the
    // Set<T>()-per-call pattern the other DbSets above use.
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Listing>(listing =>
        {
            listing.Property(l => l.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
        });
    }
}
