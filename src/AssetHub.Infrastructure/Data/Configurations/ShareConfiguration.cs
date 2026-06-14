using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class ShareConfiguration : IEntityTypeConfiguration<Share>
{
    public void Configure(EntityTypeBuilder<Share> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("idx_shares_token_hash_unique");
        entity.HasIndex(e => new { e.ScopeType, e.ScopeId }).HasDatabaseName("idx_shares_scope");
        entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("idx_shares_expires_at");
        entity.HasIndex(e => e.CreatedByUserId).HasDatabaseName("idx_shares_created_by_user_id");

        entity.Property(e => e.TokenHash).HasMaxLength(255).IsRequired();
        entity.Property(e => e.TokenEncrypted).HasMaxLength(2048);
        entity.Property(e => e.PasswordEncrypted).HasMaxLength(2048);
        entity.Property(e => e.PasswordVersion).HasDefaultValue(0).IsRequired();
        entity.Property(e => e.ScopeType)
            .HasConversion(v => v.ToDbString(), v => v.ToShareScopeType())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.PermissionsJson)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, bool>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, bool>())
            .HasColumnType(ModelConventions.Jsonb)
            .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, bool>>(
                (c1, c2) => c1 != null && c2 != null && c1.Count == c2.Count && !c1.Except(c2).Any(),
                c => c.Aggregate(0, (a, kv) => HashCode.Combine(a, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
                c => new Dictionary<string, bool>(c)));

        // Note: Asset and Collection relationships are polymorphic via ScopeType/ScopeId
        // FK constraints are enforced at application level, not DB level
        entity.Ignore(e => e.Asset);
        entity.Ignore(e => e.Collection);
    }
}
