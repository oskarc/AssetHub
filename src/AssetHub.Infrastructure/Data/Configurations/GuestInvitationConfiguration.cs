using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class GuestInvitationConfiguration : IEntityTypeConfiguration<GuestInvitation>
{
    public void Configure(EntityTypeBuilder<GuestInvitation> entity)
    {
        entity.HasKey(e => e.Id);

        // Lookup-by-token-hash on every magic-link click. Unique
        // because the plaintext is a 32-byte CSPRNG random; collision
        // is astronomically unlikely.
        entity.HasIndex(e => e.TokenHash).IsUnique()
            .HasDatabaseName("idx_guest_invitation_token_hash_unique");

        // Expiry sweep filters by ExpiresAt + RevokedAt; partial helps
        // when most invitations are long-since-resolved.
        entity.HasIndex(e => new { e.ExpiresAt, e.RevokedAt })
            .HasDatabaseName("idx_guest_invitation_expiry_revoked");

        entity.Property(e => e.Email).HasMaxLength(320).IsRequired();
        entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
        entity.Property(e => e.AcceptedUserId).HasMaxLength(255);
        entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();

        entity.Property(e => e.CollectionIds)
            .HasColumnType("uuid[]")
            .Metadata.SetValueComparer(new ValueComparer<List<Guid>>(
                (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
    }
}
