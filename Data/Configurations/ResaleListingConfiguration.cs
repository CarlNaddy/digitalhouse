using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="ResaleListing"/>.</summary>
public sealed class ResaleListingConfiguration : IEntityTypeConfiguration<ResaleListing>
{
    public void Configure(EntityTypeBuilder<ResaleListing> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(l => l.SellerId).HasMaxLength(450);

        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => l.ProductId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'");
    }
}
