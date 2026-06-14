using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_audit_created_at");
        entity.HasIndex(e => new { e.EventType, e.CreatedAt }).HasDatabaseName("idx_audit_event_type_created");
        entity.HasIndex(e => e.TargetId).HasDatabaseName("idx_audit_target_id");

        entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
        entity.Property(e => e.TargetType).HasMaxLength(100).IsRequired();
        entity.Property(e => e.DetailsJson)
            .HasConversion(ModelConventions.JsonbDictionaryConverter)
            .HasColumnType(ModelConventions.Jsonb)
            .Metadata.SetValueComparer(ModelConventions.JsonbDictionaryComparer);
    }
}
