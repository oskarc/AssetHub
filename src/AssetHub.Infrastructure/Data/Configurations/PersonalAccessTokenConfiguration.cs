using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> entity)
    {
        entity.HasKey(e => e.Id);
        // Lookup happens by hash on every PAT-authenticated request — must be unique + indexed.
        entity.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("idx_pat_token_hash_unique");
        entity.HasIndex(e => new { e.OwnerUserId, e.CreatedAt }).HasDatabaseName("idx_pat_owner_created");

        entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
        entity.Property(e => e.OwnerUserId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();

        entity.Property(e => e.Scopes)
            .HasColumnType(ModelConventions.TextArray)
            .Metadata.SetValueComparer(ModelConventions.StringListComparer);
    }
}
