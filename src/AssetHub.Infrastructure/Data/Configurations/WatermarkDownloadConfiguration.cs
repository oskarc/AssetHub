using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class WatermarkDownloadConfiguration : IEntityTypeConfiguration<WatermarkDownload>
{
    public void Configure(EntityTypeBuilder<WatermarkDownload> entity)
    {
        entity.HasKey(e => e.Id);

        entity.Property(e => e.DownloadPath).HasMaxLength(20).IsRequired();
        entity.Property(e => e.AlgorithmVersion).HasMaxLength(16).IsRequired();

        // Hot path: verify endpoint looks up by Id (PK, indexed for free).
        // Asset analytics (T5-ANL-01) joins on AssetId + DownloadedAt.
        entity.HasIndex(e => new { e.AssetId, e.DownloadedAt })
            .HasDatabaseName("idx_watermark_download_asset_downloaded_at");

        // Indexable HMAC hashes — let analytics queries find rows by recipient
        // without ever decrypting the encrypted PII columns.
        entity.HasIndex(e => e.RecipientUserIdHash)
            .HasDatabaseName("idx_watermark_download_recipient_user_hash");
        entity.HasIndex(e => e.RecipientEmailHash)
            .HasDatabaseName("idx_watermark_download_recipient_email_hash");
        entity.HasIndex(e => e.ShareId)
            .HasDatabaseName("idx_watermark_download_share");

        // Deliberately no FK to Asset / Share. Forensic rows are designed to
        // outlive their referenced entity — the verify endpoint returns the
        // historical AssetId / ShareId for hard-deleted targets so an admin
        // can still resolve "this asset USED to be ours" attribution. EF
        // doesn't auto-create FKs for scalar Guid columns without a navigation
        // property, so leaving these uncommented keeps the relationship out.
    }
}
