using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class ZipDownloadConfiguration : IEntityTypeConfiguration<ZipDownload>
{
    public void Configure(EntityTypeBuilder<ZipDownload> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.Status).HasDatabaseName("idx_zip_downloads_status");
        entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("idx_zip_downloads_expires_at");
        entity.HasIndex(e => e.RequestedByUserId).HasDatabaseName("idx_zip_downloads_user_id");

        entity.Property(e => e.Status)
            .HasConversion(v => v.ToDbString(), v => v.ToZipDownloadStatus())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.HangfireJobId).HasMaxLength(255);
        entity.Property(e => e.ZipObjectKey).HasMaxLength(512);
        entity.Property(e => e.ZipFileName).HasMaxLength(500).IsRequired();
        entity.Property(e => e.ScopeType)
            .HasConversion(v => v.ToDbString(), v => v.ToShareScopeType())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.RequestedByUserId).HasMaxLength(255);
        entity.Property(e => e.ShareTokenHash).HasMaxLength(255);
        entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
    }
}
