using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssetHub.Infrastructure.Data.Configurations;

/// <summary>
/// Brand entity configuration (T4-BP-01 — share-page theming).
///
/// Exemplar for the per-entity configuration pattern (CLAUDE.md § DbContext
/// configuration): new entities get a sealed <see cref="IEntityTypeConfiguration{T}"/>
/// class here; legacy inline blocks in <see cref="AssetHubDbContext"/> migrate
/// over entity-by-entity as they're touched. A split must produce a byte-identical
/// model — EF's PendingModelChangesWarning throws outside Development if it doesn't.
/// </summary>
public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> entity)
    {
        entity.HasKey(e => e.Id);
        // Resolution path scans for a default brand when a collection
        // has no BrandId — partial / unique-default invariant is
        // enforced in the service layer because Postgres can't
        // express "exactly one row where IsDefault = true" without
        // either a partial unique index or a trigger; partial unique
        // index is added below.
        entity.HasIndex(e => e.IsDefault)
            .HasDatabaseName("idx_brand_default")
            .HasFilter("\"IsDefault\" = true")
            .IsUnique();

        entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
        entity.Property(e => e.LogoObjectKey).HasMaxLength(512);
        entity.Property(e => e.PrimaryColor).HasMaxLength(16).IsRequired();
        entity.Property(e => e.SecondaryColor).HasMaxLength(16).IsRequired();
        entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();
    }
}
