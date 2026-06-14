using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AssetVersionConfiguration : IEntityTypeConfiguration<AssetVersion>
{
    public void Configure(EntityTypeBuilder<AssetVersion> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.AssetId, e.VersionNumber })
            .IsUnique()
            .HasDatabaseName("idx_asset_version_asset_version_unique");

        entity.Property(e => e.OriginalObjectKey).HasMaxLength(512).IsRequired();
        entity.Property(e => e.ThumbObjectKey).HasMaxLength(512);
        entity.Property(e => e.MediumObjectKey).HasMaxLength(512);
        entity.Property(e => e.PosterObjectKey).HasMaxLength(512);
        entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
        entity.Property(e => e.Sha256).HasMaxLength(64).IsRequired();
        entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.ChangeNote).HasMaxLength(1000);

        entity.Property(e => e.EditDocument).HasColumnType(ModelConventions.Jsonb);

        entity.Property(e => e.MetadataSnapshot)
            .HasConversion(ModelConventions.JsonbDictionaryConverter)
            .HasColumnType(ModelConventions.Jsonb)
            .Metadata.SetValueComparer(ModelConventions.JsonbDictionaryComparer);

        // Cascade with the Asset row so a hard purge (TTL or admin delete-forever)
        // takes the version history with it. Soft delete is hidden by Asset's global
        // query filter, which leaves AssetVersions visible — fine, they're orphans
        // until restore brings the Asset back, and we never query Versions in isolation.
        entity.HasOne(e => e.Asset)
            .WithMany(a => a.Versions)
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
