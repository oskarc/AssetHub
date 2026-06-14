using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.AssetType }).HasDatabaseName("idx_assets_type");
        entity.HasIndex(e => new { e.Status }).HasDatabaseName("idx_assets_status");
        entity.HasIndex(e => new { e.CreatedAt }).HasDatabaseName("idx_assets_created_at");
        entity.HasIndex(e => e.CreatedByUserId).HasDatabaseName("idx_assets_created_by_user_id");
        entity.HasIndex(e => e.OriginalObjectKey).HasDatabaseName("idx_assets_original_object_key");
        // Partial index — only rows in Trash get indexed. Keeps the index tiny since most
        // assets are never deleted; the purge worker queries DeletedAt < cutoff hourly.
        entity.HasIndex(e => e.DeletedAt)
            .HasDatabaseName("idx_assets_deleted_at")
            .HasFilter("\"DeletedAt\" IS NOT NULL");

        // Soft delete via global query filter. Admin trash endpoints must call
        // IgnoreQueryFilters() to see deleted rows; the purge worker does the same.
        entity.HasQueryFilter(a => a.DeletedAt == null);

        entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
        entity.Property(e => e.Description).HasMaxLength(2000);
        entity.Property(e => e.Copyright).HasMaxLength(500);
        entity.Property(e => e.AssetType)
            .HasConversion(v => v.ToDbString(), v => v.ToAssetType())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.Status)
            .HasConversion(v => v.ToDbString(), v => v.ToAssetStatus())
            .HasMaxLength(50).IsRequired();
        // No HasDefaultValue — the Draft enum is CLR default (0), which
        // would make EF treat any "set to Draft" as "unset" and override
        // with the server-side default. The C# field initializer on
        // Asset.WorkflowState covers the "brand new entity" case, and
        // the migration's AddColumn defaultValue backfills existing rows.
        entity.Property(e => e.WorkflowState)
            .HasConversion(v => v.ToDbString(), v => v.ToAssetWorkflowState())
            .HasMaxLength(50).IsRequired();
        entity.HasIndex(e => e.WorkflowState)
            .HasDatabaseName("idx_assets_workflow_state");
        entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
        entity.Property(e => e.OriginalObjectKey).HasMaxLength(512).IsRequired();
        entity.Property(e => e.ThumbObjectKey).HasMaxLength(512);
        entity.Property(e => e.MediumObjectKey).HasMaxLength(512);
        entity.Property(e => e.PosterObjectKey).HasMaxLength(512);
        entity.Property(e => e.WaveformPeaksPath).HasMaxLength(512);

        // T5-WMK-01 — opaque asset-fingerprint token. Set by background sweep;
        // null until the sweep has run, in which case downloads embed both layers
        // on-the-fly per the no-gap rule.
        entity.Property(e => e.AssetWatermarkToken).HasMaxLength(64);

        entity.Property(e => e.Tags)
            .HasColumnType(ModelConventions.TextArray)
            .Metadata.SetValueComparer(ModelConventions.StringListComparer);

        entity.Property(e => e.MetadataJson)
            .HasConversion(ModelConventions.JsonbDictionaryConverter)
            .HasColumnType(ModelConventions.Jsonb)
            .Metadata.SetValueComparer(ModelConventions.JsonbDictionaryComparer);

        // Source asset self-FK for derivative lineage
        entity.HasIndex(e => e.SourceAssetId).HasDatabaseName("idx_assets_source_asset_id");
        entity.HasOne(e => e.SourceAsset)
            .WithMany(e => e.Derivatives)
            .HasForeignKey(e => e.SourceAssetId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.Property(e => e.EditDocument).HasColumnType(ModelConventions.Jsonb);

        // Shadow SearchVector: tsvector column maintained by Postgres triggers (see migration
        // AddAssetSearchAndSavedSearch). Query via EF.Property<NpgsqlTsVector>(asset, "SearchVector").
        entity.Property<NpgsqlTsVector?>("SearchVector")
            .HasColumnName("search_vector")
            .HasColumnType("tsvector")
            .ValueGeneratedOnAddOrUpdate();

        entity.HasIndex("SearchVector")
            .HasMethod("gin")
            .HasDatabaseName("idx_asset_search_vector");
    }
}
