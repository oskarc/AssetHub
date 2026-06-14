using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class CollectionAclConfiguration : IEntityTypeConfiguration<CollectionAcl>
{
    public void Configure(EntityTypeBuilder<CollectionAcl> entity)
    {
        entity.HasKey(e => e.Id);
        entity.HasIndex(e => new { e.CollectionId }).HasDatabaseName("idx_collection_acl_collection_id");
        entity.HasIndex(e => new { e.PrincipalType, e.PrincipalId }).HasDatabaseName("idx_collection_acl_principal");
        entity.HasIndex(e => new { e.CollectionId, e.PrincipalType, e.PrincipalId })
            .IsUnique()
            .HasDatabaseName("idx_collection_acl_unique");

        entity.Property(e => e.PrincipalType)
            .HasConversion(v => v.ToDbString(), v => v.ToPrincipalType())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.PrincipalId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Role)
            .HasConversion(v => v.ToDbString(), v => v.ToAclRole())
            .HasMaxLength(50).IsRequired();

        entity.HasOne(e => e.Collection)
            .WithMany(e => e.Acls)
            .HasForeignKey(e => e.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
