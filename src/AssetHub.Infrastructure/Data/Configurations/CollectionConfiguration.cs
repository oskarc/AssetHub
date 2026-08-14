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
    }
}
