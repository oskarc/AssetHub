using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.HasKey(e => e.Id);

        // Drainer picks undispatched rows oldest-first within the attempt cap.
        // DispatchedAt is in the index so the partial-style filter can be
        // expressed via the leading column without a partial-index migration.
        entity.HasIndex(e => new { e.DispatchedAt, e.AttemptCount, e.CreatedAt })
            .HasDatabaseName("idx_outbox_pending");

        entity.Property(e => e.MessageType).HasMaxLength(500).IsRequired();
        entity.Property(e => e.PayloadJson).HasColumnType("text").IsRequired();
        entity.Property(e => e.LastError).HasMaxLength(1000);
    }
}
