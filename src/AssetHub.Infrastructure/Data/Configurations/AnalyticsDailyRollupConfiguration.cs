using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AnalyticsDailyRollupConfiguration : IEntityTypeConfiguration<AnalyticsDailyRollup>
{
    public void Configure(EntityTypeBuilder<AnalyticsDailyRollup> entity)
    {
        // Composite PK: each (date, metric, entity) tuple has at most one row.
        // Upserts target this PK so the rollup is idempotent on re-run.
        entity.ToTable("AnalyticsDailyRollups");
        entity.HasKey(e => new { e.Date, e.Metric, e.EntityId })
            .HasName("pk_analytics_daily_rollups");

        entity.Property(e => e.Metric)
            .HasConversion(v => v.ToDbString(), v => v.ToAnalyticsCountMetric())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.EntityId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Count).IsRequired();
        entity.Property(e => e.UpdatedAt).IsRequired();

        // Date+Metric is the dashboard's hot-path query shape.
        entity.HasIndex(e => new { e.Date, e.Metric })
            .HasDatabaseName("idx_analytics_daily_date_metric");
    }
}
