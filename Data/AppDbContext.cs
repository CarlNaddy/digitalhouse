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

    // Marketplace (openspec: add-digital-asset-marketplace).
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<AssetOwnership> AssetOwnerships => Set<AssetOwnership>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ResaleListing> ResaleListings => Set<ResaleListing>();
    public DbSet<PricePoint> PricePoints => Set<PricePoint>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<StripePayment> StripePayments => Set<StripePayment>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();

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

        // IEntityTypeConfiguration<T> classes under Data/Configurations/ —
        // used for the marketplace entities, which carry enough mapping to
        // warrant their own files.
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // --- Marketplace: Product immutability gate (openspec: add-digital-asset-marketplace) ---
    //
    // Product.PriceSeed / CreatedAt / PublicId are set once at creation and
    // never change — a modified value on an already-persisted row is a bug, so
    // the save is rejected. Product.CurrentPriceMicros is a cache written only
    // by the pricing engine, which wraps its save in AllowPriceWrites(); any
    // other path that dirties it is rejected. The flag is an instance field:
    // the engine's `using` block and its SaveChangesAsync run on the same
    // context instance.

    private bool _allowPriceWrites;

    /// <summary>
    /// Opens a scope in which <see cref="Product.CurrentPriceMicros"/> may be
    /// written on this context. Only the pricing engine should call this.
    /// </summary>
    public IDisposable AllowPriceWrites() => new PriceWriteScope(this);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardProductImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardProductImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardProductImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<Product>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            foreach (var name in _immutableProductProperties)
            {
                if (entry.Property(name).IsModified)
                {
                    throw new InvalidOperationException(
                        $"Product.{name} is immutable and cannot be changed after creation.");
                }
            }

            if (!_allowPriceWrites && entry.Property(nameof(Product.CurrentPriceMicros)).IsModified)
            {
                throw new InvalidOperationException(
                    "Product.CurrentPriceMicros is written only by the pricing engine. "
                    + "Wrap the save in AppDbContext.AllowPriceWrites().");
            }
        }
    }

    private static readonly string[] _immutableProductProperties =
    [
        nameof(Product.PriceSeed),
        nameof(Product.CreatedAt),
        nameof(Product.PublicId),
    ];

    private sealed class PriceWriteScope : IDisposable
    {
        private readonly AppDbContext _context;
        private readonly bool _previous;

        public PriceWriteScope(AppDbContext context)
        {
            _context = context;
            _previous = context._allowPriceWrites;
            context._allowPriceWrites = true;
        }

        public void Dispose() => _context._allowPriceWrites = _previous;
    }
}
