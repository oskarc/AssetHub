using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class OrphanedObjectConfiguration : IEntityTypeConfiguration<OrphanedObject>
{
    public void Configure(EntityTypeBuilder<OrphanedObject> entity)
    {
        entity.HasKey(e => e.Id);

        // Sweeper picks oldest-first within the attempt cap.
        entity.HasIndex(e => new { e.AttemptCount, e.CreatedAt })
            .HasDatabaseName("idx_orphaned_object_attempts_created");

        entity.Property(e => e.BucketName).HasMaxLength(100).IsRequired();
        entity.Property(e => e.ObjectKey).HasMaxLength(512).IsRequired();
        entity.Property(e => e.LastError).HasMaxLength(1000);
    }
}
