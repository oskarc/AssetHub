using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AnalyticsStorageRollupConfiguration : IEntityTypeConfiguration<AnalyticsStorageRollup>
{
    public void Configure(EntityTypeBuilder<AnalyticsStorageRollup> entity)
    {
        entity.ToTable("AnalyticsStorageRollups");
        entity.HasKey(e => new { e.Date, e.Metric, e.EntityId })
            .HasName("pk_analytics_storage_rollups");

        entity.Property(e => e.Metric)
            .HasConversion(v => v.ToDbString(), v => v.ToAnalyticsStorageMetric())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.EntityId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Bytes).IsRequired();
        entity.Property(e => e.AssetCount).IsRequired();
        entity.Property(e => e.UpdatedAt).IsRequired();

        entity.HasIndex(e => new { e.Date, e.Metric })
            .HasDatabaseName("idx_analytics_storage_date_metric");
    }
}
