using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="Product"/>.</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(p => p.PublicId)
            .HasConversion(new ValueConverter<Ulid, string>(
                v => v.ToString(),
                s => Ulid.Parse(s, CultureInfo.InvariantCulture)))
            .HasColumnType("character(26)")
            .IsFixedLength()
            .HasMaxLength(26)
            .IsRequired();

        builder.HasIndex(p => p.PublicId).IsUnique();
        builder.HasIndex(p => p.Slug).IsUnique();

        builder.Property(p => p.Slug).HasMaxLength(140).IsRequired();
        builder.Property(p => p.Title).HasMaxLength(140).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(4000);

        builder.HasMany(p => p.Images)
            .WithOne(i => i.Product)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
