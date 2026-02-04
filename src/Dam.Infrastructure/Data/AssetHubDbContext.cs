using Dam.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dam.Infrastructure.Data;

public class AssetHubDbContext : DbContext
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Collection
        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ParentId }).HasName("idx_collections_parent_id");
            entity.HasIndex(e => new { e.Name }).HasName("idx_collections_name");

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
            entity.HasIndex(e => new { e.CollectionId }).HasName("idx_collection_acl_collection_id");
            entity.HasIndex(e => new { e.PrincipalType, e.PrincipalId }).HasName("idx_collection_acl_principal");

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
            entity.HasIndex(e => new { e.CollectionId }).HasName("idx_assets_collection_id");
            entity.HasIndex(e => new { e.AssetType }).HasName("idx_assets_type");
            entity.HasIndex(e => new { e.Status }).HasName("idx_assets_status");
            entity.HasIndex(e => new { e.CreatedAt }).HasName("idx_assets_created_at");

            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.AssetType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.OriginalObjectKey).HasMaxLength(512).IsRequired();
            entity.Property(e => e.ThumbObjectKey).HasMaxLength(512);
            entity.Property(e => e.MediumObjectKey).HasMaxLength(512);
            entity.Property(e => e.PosterObjectKey).HasMaxLength(512);

            entity.Property(e => e.Tags).HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());

            entity.Property(e => e.MetadataJson).HasColumnType("jsonb");

            entity.HasOne(e => e.Collection)
                .WithMany(e => e.Assets)
                .HasForeignKey(e => e.CollectionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AssetCollection (many-to-many join table)
        modelBuilder.Entity<AssetCollection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AssetId, e.CollectionId }).IsUnique().HasName("idx_asset_collection_unique");
            entity.HasIndex(e => e.CollectionId).HasName("idx_asset_collection_collection_id");

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
            entity.HasIndex(e => e.TokenHash).IsUnique().HasName("idx_shares_token_hash_unique");
            entity.HasIndex(e => new { e.ScopeType, e.ScopeId }).HasName("idx_shares_scope");

            entity.Property(e => e.TokenHash).HasMaxLength(255).IsRequired();
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
            entity.HasIndex(e => e.CreatedAt).HasName("idx_audit_created_at");
            entity.HasIndex(e => new { e.EventType, e.CreatedAt }).HasName("idx_audit_event_type_created");

            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TargetType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DetailsJson).HasColumnType("jsonb");
        });
    }
}
