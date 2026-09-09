using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="AssetOwnership"/>.</summary>
public sealed class AssetOwnershipConfiguration : IEntityTypeConfiguration<AssetOwnership>
{
    public void Configure(EntityTypeBuilder<AssetOwnership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(o => o.AcquisitionType).HasConversion<string>().HasMaxLength(24);
        builder.Property(o => o.UserId).HasMaxLength(450);

        builder.HasOne(o => o.Product)
            .WithMany()
            .HasForeignKey(o => o.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Exactly one current holder per product.
        builder.HasIndex(o => o.ProductId)
            .IsUnique()
            .HasFilter("\"ReleasedAt\" IS NULL");

        builder.HasIndex(o => new { o.UserId, o.ProductId });
    }
}
