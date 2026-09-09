using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="Reservation"/>.</summary>
public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.UserId).HasMaxLength(450);

        builder.HasOne(r => r.Product)
            .WithMany()
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // At most one live hold per product — the primary serializer for
        // concurrent buyers.
        builder.HasIndex(r => r.ProductId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'");
    }
}
