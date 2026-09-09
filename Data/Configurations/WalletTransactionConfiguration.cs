using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DigitalHouse.Data.Configurations;

/// <summary>EF Core mapping for <see cref="WalletTransaction"/> (append-only).</summary>
public sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(24);
        builder.Property(t => t.ReferenceKind).HasConversion<string>().HasMaxLength(16);

        // Meta is a small free-form string map persisted as jsonb.
        var metaConverter = new ValueConverter<Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default)
                 ?? new Dictionary<string, string>());

        var metaComparer = new ValueComparer<Dictionary<string, string>>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default)
                   == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default)
                .GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(
                     JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default)
                 ?? new Dictionary<string, string>());

        builder.Property(t => t.Meta)
            .HasColumnType("jsonb")
            .HasConversion(metaConverter, metaComparer);

        builder.HasIndex(t => new { t.WalletId, t.CreatedAt });
    }
}
