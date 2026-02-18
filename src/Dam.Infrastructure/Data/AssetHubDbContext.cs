using System.Text.Json;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Dam.Infrastructure.Data;

public class AssetHubDbContext : DbContext, IDataProtectionKeyContext
{
    public AssetHubDbContext(DbContextOptions<AssetHubDbContext> options) : base(options)
    {
    }

    public DbSet<Collection> Collections { get; set; } = null!;
    public DbSet<CollectionAcl> CollectionAcls { get; set; } = null!;
    public DbSet<Asset> Assets { get; set; } = null!;
    public DbSet<AssetCollection> AssetCollections { get; set; } = null!;
    public DbSet<Share> Shares { get; set; } = null!;
    public DbSet<AuditEvent> AuditEvents { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Collection
        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ParentId }).HasDatabaseName("idx_collections_parent_id");
            entity.HasIndex(e => new { e.Name }).HasDatabaseName("idx_collections_name");

            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);

            entity.HasOne(e => e.Parent)
                .WithMany(e => e.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CollectionAcl
        modelBuilder.Entity<CollectionAcl>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CollectionId }).HasDatabaseName("idx_collection_acl_collection_id");
            entity.HasIndex(e => new { e.PrincipalType, e.PrincipalId }).HasDatabaseName("idx_collection_acl_principal");
            entity.HasIndex(e => new { e.CollectionId, e.PrincipalType, e.PrincipalId })
                .IsUnique()
                .HasDatabaseName("idx_collection_acl_unique");

            entity.Property(e => e.PrincipalType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PrincipalId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();

            entity.HasOne(e => e.Collection)
                .WithMany(e => e.Acls)
                .HasForeignKey(e => e.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Asset
        modelBuilder.Entity<Asset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AssetType }).HasDatabaseName("idx_assets_type");
            entity.HasIndex(e => new { e.Status }).HasDatabaseName("idx_assets_status");
            entity.HasIndex(e => new { e.CreatedAt }).HasDatabaseName("idx_assets_created_at");
            entity.HasIndex(e => e.CreatedByUserId).HasDatabaseName("idx_assets_created_by_user_id");

            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Copyright).HasMaxLength(500);
            entity.Property(e => e.AssetType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.OriginalObjectKey).HasMaxLength(512).IsRequired();
            entity.Property(e => e.ThumbObjectKey).HasMaxLength(512);
            entity.Property(e => e.MediumObjectKey).HasMaxLength(512);
            entity.Property(e => e.PosterObjectKey).HasMaxLength(512);

            entity.Property(e => e.Tags).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                    (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));

            entity.Property(e => e.MetadataJson).HasColumnType("jsonb");
        });

        // AssetCollection (many-to-many join table)
        modelBuilder.Entity<AssetCollection>(entity =>
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
        });

        // Share
        modelBuilder.Entity<Share>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("idx_shares_token_hash_unique");
            entity.HasIndex(e => new { e.ScopeType, e.ScopeId }).HasDatabaseName("idx_shares_scope");
            entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("idx_shares_expires_at");
            entity.HasIndex(e => e.CreatedByUserId).HasDatabaseName("idx_shares_created_by_user_id");

            entity.Property(e => e.TokenHash).HasMaxLength(255).IsRequired();
            entity.Property(e => e.TokenEncrypted).HasMaxLength(2048);
            entity.Property(e => e.ScopeType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PermissionsJson).HasColumnType("jsonb");

            // Note: Asset and Collection relationships are polymorphic via ScopeType/ScopeId
            // FK constraints are enforced at application level, not DB level
            entity.Ignore(e => e.Asset);
            entity.Ignore(e => e.Collection);
        });

        // AuditEvent
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_audit_created_at");
            entity.HasIndex(e => new { e.EventType, e.CreatedAt }).HasDatabaseName("idx_audit_event_type_created");
            entity.HasIndex(e => e.TargetId).HasDatabaseName("idx_audit_target_id");

            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TargetType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DetailsJson).HasColumnType("jsonb");
        });
    }
}
