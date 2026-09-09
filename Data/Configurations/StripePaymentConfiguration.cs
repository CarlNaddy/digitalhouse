using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="StripePayment"/>.</summary>
public sealed class StripePaymentConfiguration : IEntityTypeConfiguration<StripePayment>
{
    public void Configure(EntityTypeBuilder<StripePayment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(p => p.UserId).HasMaxLength(450);
        builder.Property(p => p.PaymentIntentId).HasMaxLength(255);
        builder.Property(p => p.Status).HasMaxLength(40);

        builder.HasIndex(p => p.PaymentIntentId).IsUnique();
    }
}
