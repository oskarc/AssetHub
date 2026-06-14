using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AssetMetadataValueConfiguration : IEntityTypeConfiguration<AssetMetadataValue>
{
    public void Configure(EntityTypeBuilder<AssetMetadataValue> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.MetadataFieldId, e.AssetId }).HasDatabaseName("idx_asset_metadata_values_field_asset");
        entity.HasIndex(e => e.AssetId).HasDatabaseName("idx_asset_metadata_values_asset");
        entity.HasIndex(e => e.ValueTaxonomyTermId)
            .HasFilter("\"ValueTaxonomyTermId\" IS NOT NULL")
            .HasDatabaseName("idx_asset_metadata_values_taxonomy_term");

        entity.Property(e => e.ValueText).HasMaxLength(4000);
        entity.Property(e => e.ValueNumeric).HasColumnType("numeric");

        entity.HasOne(e => e.Asset)
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.MetadataField)
            .WithMany()
            .HasForeignKey(e => e.MetadataFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.ValueTaxonomyTerm)
            .WithMany()
            .HasForeignKey(e => e.ValueTaxonomyTermId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
