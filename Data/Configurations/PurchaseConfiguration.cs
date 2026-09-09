using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="Purchase"/>.</summary>
public sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.BuyerId).HasMaxLength(450);
        builder.Property(p => p.SellerId).HasMaxLength(450);
        builder.Property(p => p.IdempotencyKey).HasMaxLength(200);
        builder.Property(p => p.StripePaymentIntentId).HasMaxLength(255);

        builder.HasOne(p => p.Product)
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.IdempotencyKey).IsUnique();
    }
}
