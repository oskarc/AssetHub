using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class TaxonomyConfiguration : IEntityTypeConfiguration<Taxonomy>
{
    public void Configure(EntityTypeBuilder<Taxonomy> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("idx_taxonomies_name_unique");

        entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Description).HasMaxLength(1000);
        entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();

        entity.HasMany(e => e.Terms)
            .WithOne(t => t.Taxonomy)
            .HasForeignKey(t => t.TaxonomyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
