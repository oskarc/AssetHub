using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class SavedSearchConfiguration : IEntityTypeConfiguration<SavedSearch>
{
    public void Configure(EntityTypeBuilder<SavedSearch> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.OwnerUserId).HasDatabaseName("idx_saved_searches_owner");
        entity.HasIndex(e => new { e.OwnerUserId, e.Name }).IsUnique().HasDatabaseName("idx_saved_searches_owner_name_unique");

        entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
        entity.Property(e => e.OwnerUserId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.RequestJson).HasColumnType(ModelConventions.Jsonb).IsRequired();
        entity.Property(e => e.Notify)
            .HasConversion(v => v.ToDbString(), v => v.ToSavedSearchNotifyCadence())
            .HasMaxLength(50).IsRequired();
    }
}
