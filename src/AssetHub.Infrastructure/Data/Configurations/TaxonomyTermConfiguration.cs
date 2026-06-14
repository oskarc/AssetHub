using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class TaxonomyTermConfiguration : IEntityTypeConfiguration<TaxonomyTerm>
{
    public void Configure(EntityTypeBuilder<TaxonomyTerm> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.TaxonomyId, e.Slug }).IsUnique().HasDatabaseName("idx_taxonomy_terms_taxonomy_slug_unique");
        entity.HasIndex(e => new { e.TaxonomyId, e.SortOrder }).HasDatabaseName("idx_taxonomy_terms_taxonomy_sort");
        entity.HasIndex(e => e.ParentTermId).HasDatabaseName("idx_taxonomy_terms_parent");

        entity.Property(e => e.Label).HasMaxLength(255).IsRequired();
        entity.Property(e => e.LabelSv).HasMaxLength(255);
        entity.Property(e => e.Slug).HasMaxLength(255).IsRequired();

        entity.HasOne(e => e.ParentTerm)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentTermId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
