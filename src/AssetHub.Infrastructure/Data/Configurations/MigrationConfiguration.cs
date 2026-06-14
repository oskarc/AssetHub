using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class MigrationConfiguration : IEntityTypeConfiguration<Migration>
{
    public void Configure(EntityTypeBuilder<Migration> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.Status).HasDatabaseName("idx_migrations_status");
        entity.HasIndex(e => e.CreatedByUserId).HasDatabaseName("idx_migrations_created_by");

        entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
        entity.Property(e => e.SourceType)
            .HasConversion(v => v.ToDbString(), v => v.ToMigrationSourceType())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.Status)
            .HasConversion(v => v.ToDbString(), v => v.ToMigrationStatus())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();

        entity.Property(e => e.SourceConfig)
            .HasConversion(ModelConventions.JsonbDictionaryConverter)
            .HasColumnType(ModelConventions.Jsonb)
            .Metadata.SetValueComparer(ModelConventions.JsonbDictionaryComparer);

        entity.Property(e => e.FieldMapping)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>())
            .HasColumnType(ModelConventions.Jsonb)
            .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>>(
                (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
                c => JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
                c => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));

        entity.HasMany(e => e.Items)
            .WithOne(i => i.Migration)
            .HasForeignKey(i => i.MigrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
