using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="ProductImage"/>.</summary>
public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasOne<StoredFile>()
            .WithMany()
            .HasForeignKey(i => i.StoredFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.ProductId, i.Position });
    }
}
