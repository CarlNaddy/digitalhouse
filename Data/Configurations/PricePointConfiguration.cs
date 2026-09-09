using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="PricePoint"/> (append-only).</summary>
public sealed class PricePointConfiguration : IEntityTypeConfiguration<PricePoint>
{
    public void Configure(EntityTypeBuilder<PricePoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(p => p.Cause).HasConversion<string>().HasMaxLength(16);

        builder.HasOne(p => p.Product)
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.ProductId, p.CapturedAt });
    }
}
