using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.Name }).IsUnique().HasDatabaseName("idx_collections_name_unique");

        entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Description).HasMaxLength(1000);

        // T4-BP-01 — optional FK to a Brand for share-page styling.
        // SetNull on delete: removing a brand quietly demotes every
        // collection that referenced it back to the default theme.
        entity.HasIndex(e => e.BrandId).HasDatabaseName("idx_collections_brand_id");
        entity.HasOne(e => e.Brand)
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.SetNull);

        // T5-NEST-01 — optional self-FK for nested collections.
        // SetNull on delete: deleting a parent orphans children to root
        // rather than cascading the delete (collections aren't
        // soft-deletable yet, and a cascade would be unrecoverable).
        // Cycle prevention is at the service layer, not the DB.
        entity.HasIndex(e => e.ParentCollectionId).HasDatabaseName("idx_collections_parent_id");
        entity.HasOne(e => e.Parent)
            .WithMany(p => p.Children)
            .HasForeignKey(e => e.ParentCollectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
