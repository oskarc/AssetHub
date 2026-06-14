using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class MetadataSchemaConfiguration : IEntityTypeConfiguration<MetadataSchema>
{
    public void Configure(EntityTypeBuilder<MetadataSchema> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("idx_metadata_schemas_name_unique");
        entity.HasIndex(e => e.Scope).HasDatabaseName("idx_metadata_schemas_scope");
        entity.HasIndex(e => e.CollectionId).HasDatabaseName("idx_metadata_schemas_collection_id");

        entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Description).HasMaxLength(1000);
        entity.Property(e => e.Scope)
            .HasConversion(v => v.ToDbString(), v => v.ToMetadataSchemaScope())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.AssetType)
            .HasConversion(
                v => v.HasValue ? v.Value.ToDbString() : null,
                v => v != null ? v.ToAssetType() : null)
            .HasMaxLength(50);
        entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();

        entity.HasOne<Collection>()
            .WithMany()
            .HasForeignKey(e => e.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.Fields)
            .WithOne(f => f.MetadataSchema)
            .HasForeignKey(f => f.MetadataSchemaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
