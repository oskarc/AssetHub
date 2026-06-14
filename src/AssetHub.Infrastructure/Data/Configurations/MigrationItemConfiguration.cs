using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class MigrationItemConfiguration : IEntityTypeConfiguration<MigrationItem>
{
    public void Configure(EntityTypeBuilder<MigrationItem> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.MigrationId, e.Status }).HasDatabaseName("idx_migration_items_migration_status");
        entity.HasIndex(e => new { e.MigrationId, e.RowNumber }).HasDatabaseName("idx_migration_items_migration_row");
        entity.HasIndex(e => e.IdempotencyKey).IsUnique().HasDatabaseName("idx_migration_items_idempotency_unique");

        entity.Property(e => e.Status)
            .HasConversion(v => v.ToDbString(), v => v.ToMigrationItemStatus())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.ExternalId).HasMaxLength(255);
        entity.Property(e => e.IdempotencyKey).HasMaxLength(128).IsRequired();
        entity.Property(e => e.FileName).HasMaxLength(512).IsRequired();
        entity.Property(e => e.SourcePath).HasMaxLength(1024);
        entity.Property(e => e.Title).HasMaxLength(255);
        entity.Property(e => e.Description).HasMaxLength(2000);
        entity.Property(e => e.Copyright).HasMaxLength(500);
        entity.Property(e => e.Sha256).HasMaxLength(64);
        entity.Property(e => e.ErrorCode).HasMaxLength(100);
        entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
        entity.Property(e => e.IsFileStaged).HasDefaultValue(false);

        entity.Property(e => e.Tags)
            .HasColumnType(ModelConventions.TextArray)
            .Metadata.SetValueComparer(ModelConventions.StringListComparer);

        entity.Property(e => e.CollectionNames)
            .HasColumnType(ModelConventions.TextArray)
            .Metadata.SetValueComparer(ModelConventions.StringListComparer);

        entity.Property(e => e.MetadataJson)
            .HasConversion(ModelConventions.JsonbDictionaryConverter)
            .HasColumnType(ModelConventions.Jsonb)
            .Metadata.SetValueComparer(ModelConventions.JsonbDictionaryComparer);
    }
}
