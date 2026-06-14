using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class MetadataFieldConfiguration : IEntityTypeConfiguration<MetadataField>
{
    public void Configure(EntityTypeBuilder<MetadataField> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.MetadataSchemaId, e.Key }).IsUnique().HasDatabaseName("idx_metadata_fields_schema_key_unique");
        entity.HasIndex(e => new { e.MetadataSchemaId, e.SortOrder }).HasDatabaseName("idx_metadata_fields_schema_sort");

        entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
        entity.Property(e => e.Label).HasMaxLength(255).IsRequired();
        entity.Property(e => e.LabelSv).HasMaxLength(255);
        entity.Property(e => e.Type)
            .HasConversion(v => v.ToDbString(), v => v.ToMetadataFieldType())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.PatternRegex).HasMaxLength(500);
        entity.Property(e => e.NumericMin).HasColumnType("numeric");
        entity.Property(e => e.NumericMax).HasColumnType("numeric");

        entity.Property(e => e.SelectOptions)
            .HasColumnType(ModelConventions.TextArray)
            .Metadata.SetValueComparer(ModelConventions.StringListComparer);

        entity.HasOne(e => e.Taxonomy)
            .WithMany()
            .HasForeignKey(e => e.TaxonomyId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
