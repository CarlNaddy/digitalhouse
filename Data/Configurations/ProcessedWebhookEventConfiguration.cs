using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="ProcessedWebhookEvent"/>.</summary>
public sealed class ProcessedWebhookEventConfiguration : IEntityTypeConfiguration<ProcessedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).HasMaxLength(255);
        builder.Property(e => e.EventType).HasMaxLength(80);
    }
}
