using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AssetCollectionConfiguration : IEntityTypeConfiguration<AssetCollection>
{
    public void Configure(EntityTypeBuilder<AssetCollection> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.AssetId, e.CollectionId }).IsUnique().HasDatabaseName("idx_asset_collection_unique");
        entity.HasIndex(e => e.CollectionId).HasDatabaseName("idx_asset_collection_collection_id");

        entity.Property(e => e.AddedByUserId).HasMaxLength(255);

        entity.HasOne(e => e.Asset)
            .WithMany(e => e.AssetCollections)
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(e => e.Collection)
            .WithMany(e => e.AssetCollections)
            .HasForeignKey(e => e.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
