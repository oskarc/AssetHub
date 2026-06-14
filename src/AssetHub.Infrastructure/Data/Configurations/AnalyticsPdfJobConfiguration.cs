using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AnalyticsPdfJobConfiguration : IEntityTypeConfiguration<AnalyticsPdfJob>
{
    public void Configure(EntityTypeBuilder<AnalyticsPdfJob> entity)
    {
        entity.ToTable("AnalyticsPdfJobs");
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.Status).HasDatabaseName("idx_analytics_pdf_jobs_status");
        entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("idx_analytics_pdf_jobs_expires_at");

        entity.Property(e => e.Status)
            .HasConversion(v => v.ToDbString(), v => v.ToAnalyticsPdfJobStatus())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.RequestedByUserId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.ObjectKey).HasMaxLength(512);
        entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
    }
}
