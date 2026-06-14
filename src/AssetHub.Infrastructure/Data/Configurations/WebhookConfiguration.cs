using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class WebhookConfiguration : IEntityTypeConfiguration<Webhook>
{
    public void Configure(EntityTypeBuilder<Webhook> entity)
    {
        entity.HasKey(e => e.Id);
        // Composite index keeps the publisher's "active subscribers for this event"
        // scan cheap. A filtered index could shave bytes but isn't worth the noise.
        entity.HasIndex(e => e.IsActive)
            .HasDatabaseName("idx_webhook_active");

        entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
        entity.Property(e => e.Url).HasMaxLength(2048).IsRequired();
        entity.Property(e => e.SecretEncrypted).HasMaxLength(2048).IsRequired();
        entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();

        entity.Property(e => e.EventTypes)
            .HasColumnType(ModelConventions.TextArray)
            .Metadata.SetValueComparer(ModelConventions.StringListComparer);
    }
}
