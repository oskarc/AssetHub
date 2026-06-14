using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> entity)
    {
        entity.HasKey(e => e.Id);

        // Admin "recent failures" list filters by status + ordered newest first.
        entity.HasIndex(e => new { e.WebhookId, e.CreatedAt })
            .HasDatabaseName("idx_webhook_delivery_webhook_created");
        entity.HasIndex(e => e.Status)
            .HasDatabaseName("idx_webhook_delivery_status");

        entity.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        entity.Property(e => e.PayloadJson).HasColumnType(ModelConventions.Jsonb).IsRequired();
        entity.Property(e => e.LastError).HasMaxLength(2000);

        entity.Property(e => e.Status)
            .HasConversion(v => v.ToDbString(), v => v.ToWebhookDeliveryStatus())
            .HasMaxLength(20).IsRequired();

        entity.HasOne(e => e.Webhook)
            .WithMany()
            .HasForeignKey(e => e.WebhookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
