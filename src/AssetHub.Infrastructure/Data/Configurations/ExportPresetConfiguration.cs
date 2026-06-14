using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class ExportPresetConfiguration : IEntityTypeConfiguration<ExportPreset>
{
    public void Configure(EntityTypeBuilder<ExportPreset> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("idx_export_presets_name_unique");

        entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
        entity.Property(e => e.FitMode)
            .HasConversion(v => v.ToDbString(), v => v.ToExportPresetFitMode())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.Format)
            .HasConversion(v => v.ToDbString(), v => v.ToExportPresetFormat())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();
    }
}
