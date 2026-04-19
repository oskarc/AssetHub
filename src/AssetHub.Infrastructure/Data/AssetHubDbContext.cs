using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data;

public class AssetHubDbContext : DbContext, IDataProtectionKeyContext
{
    private const string Jsonb = "jsonb";

    public AssetHubDbContext(DbContextOptions<AssetHubDbContext> options) : base(options)
    {
    }

    public DbSet<Collection> Collections { get; set; } = null!;
    public DbSet<CollectionAcl> CollectionAcls { get; set; } = null!;
    public DbSet<Asset> Assets { get; set; } = null!;
    public DbSet<AssetCollection> AssetCollections { get; set; } = null!;
    public DbSet<Share> Shares { get; set; } = null!;
    public DbSet<AuditEvent> AuditEvents { get; set; } = null!;
    public DbSet<ZipDownload> ZipDownloads { get; set; } = null!;
    public DbSet<ExportPreset> ExportPresets { get; set; } = null!;
    public DbSet<Migration> Migrations { get; set; } = null!;
    public DbSet<MigrationItem> MigrationItems { get; set; } = null!;
    public DbSet<MetadataSchema> MetadataSchemas { get; set; } = null!;
    public DbSet<MetadataField> MetadataFields { get; set; } = null!;
    public DbSet<Taxonomy> Taxonomies { get; set; } = null!;
    public DbSet<TaxonomyTerm> TaxonomyTerms { get; set; } = null!;
    public DbSet<AssetMetadataValue> AssetMetadataValues { get; set; } = null!;
    public DbSet<SavedSearch> SavedSearches { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Collection
        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Name }).IsUnique().HasDatabaseName("idx_collections_name_unique");

            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
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
        });

        // Asset
        modelBuilder.Entity<Asset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AssetType }).HasDatabaseName("idx_assets_type");
            entity.HasIndex(e => new { e.Status }).HasDatabaseName("idx_assets_status");
            entity.HasIndex(e => new { e.CreatedAt }).HasDatabaseName("idx_assets_created_at");
            entity.HasIndex(e => e.CreatedByUserId).HasDatabaseName("idx_assets_created_by_user_id");
            entity.HasIndex(e => e.OriginalObjectKey).HasDatabaseName("idx_assets_original_object_key");

            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Copyright).HasMaxLength(500);
            entity.Property(e => e.AssetType)
                .HasConversion(v => v.ToDbString(), v => v.ToAssetType())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status)
                .HasConversion(v => v.ToDbString(), v => v.ToAssetStatus())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.OriginalObjectKey).HasMaxLength(512).IsRequired();
            entity.Property(e => e.ThumbObjectKey).HasMaxLength(512);
            entity.Property(e => e.MediumObjectKey).HasMaxLength(512);
            entity.Property(e => e.PosterObjectKey).HasMaxLength(512);

            entity.Property(e => e.Tags)
                .HasColumnType("text[]")
                .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                    (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));

            entity.Property(e => e.MetadataJson)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>())
                .HasColumnType(Jsonb)
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, object>>(
                    (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
                    c => JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
                    c => JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));

            // Source asset self-FK for derivative lineage
            entity.HasIndex(e => e.SourceAssetId).HasDatabaseName("idx_assets_source_asset_id");
            entity.HasOne(e => e.SourceAsset)
                .WithMany(e => e.Derivatives)
                .HasForeignKey(e => e.SourceAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.EditDocument).HasColumnType(Jsonb);

            // Shadow SearchVector: tsvector column maintained by Postgres triggers (see migration
            // AddAssetSearchAndSavedSearch). Query via EF.Property<NpgsqlTsVector>(asset, "SearchVector").
            entity.Property<NpgsqlTsVector?>("SearchVector")
                .HasColumnName("search_vector")
                .HasColumnType("tsvector")
                .ValueGeneratedOnAddOrUpdate();

            entity.HasIndex("SearchVector")
                .HasMethod("gin")
                .HasDatabaseName("idx_asset_search_vector");
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
            entity.Property(e => e.PasswordEncrypted).HasMaxLength(2048);
            entity.Property(e => e.ScopeType)
                .HasConversion(v => v.ToDbString(), v => v.ToShareScopeType())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.PermissionsJson)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, bool>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, bool>())
                .HasColumnType(Jsonb)
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, bool>>(
                    (c1, c2) => c1 != null && c2 != null && c1.Count == c2.Count && !c1.Except(c2).Any(),
                    c => c.Aggregate(0, (a, kv) => HashCode.Combine(a, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
                    c => new Dictionary<string, bool>(c)));

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
            entity.Property(e => e.DetailsJson)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>())
                .HasColumnType(Jsonb)
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, object>>(
                    (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
                    c => JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
                    c => JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
        });

        // ZipDownload
        modelBuilder.Entity<ZipDownload>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_zip_downloads_status");
            entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("idx_zip_downloads_expires_at");
            entity.HasIndex(e => e.RequestedByUserId).HasDatabaseName("idx_zip_downloads_user_id");

            entity.Property(e => e.Status)
                .HasConversion(v => v.ToDbString(), v => v.ToZipDownloadStatus())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.HangfireJobId).HasMaxLength(255);
            entity.Property(e => e.ZipObjectKey).HasMaxLength(512);
            entity.Property(e => e.ZipFileName).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ScopeType)
                .HasConversion(v => v.ToDbString(), v => v.ToShareScopeType())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.RequestedByUserId).HasMaxLength(255);
            entity.Property(e => e.ShareTokenHash).HasMaxLength(255);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
        });

        // ExportPreset
        modelBuilder.Entity<ExportPreset>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("idx_export_presets_name_unique");

            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.FitMode)
                .HasConversion(v => v.ToDbString(), v => v.ToExportPresetFitMode())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.Format)
                .HasConversion(v => v.ToDbString(), v => v.ToExportPresetFormat())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();
        });

        // Migration
        modelBuilder.Entity<Migration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_migrations_status");
            entity.HasIndex(e => e.CreatedByUserId).HasDatabaseName("idx_migrations_created_by");

            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.SourceType)
                .HasConversion(v => v.ToDbString(), v => v.ToMigrationSourceType())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status)
                .HasConversion(v => v.ToDbString(), v => v.ToMigrationStatus())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();

            entity.Property(e => e.SourceConfig)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>())
                .HasColumnType(Jsonb)
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, object>>(
                    (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
                    c => JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
                    c => JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));

            entity.Property(e => e.FieldMapping)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>())
                .HasColumnType(Jsonb)
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>>(
                    (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
                    c => JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
                    c => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));

            entity.HasMany(e => e.Items)
                .WithOne(i => i.Migration)
                .HasForeignKey(i => i.MigrationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MigrationItem
        modelBuilder.Entity<MigrationItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MigrationId, e.Status }).HasDatabaseName("idx_migration_items_migration_status");
            entity.HasIndex(e => new { e.MigrationId, e.RowNumber }).HasDatabaseName("idx_migration_items_migration_row");
            entity.HasIndex(e => e.IdempotencyKey).IsUnique().HasDatabaseName("idx_migration_items_idempotency_unique");

            entity.Property(e => e.Status)
                .HasConversion(v => v.ToDbString(), v => v.ToMigrationItemStatus())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(255);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(512).IsRequired();
            entity.Property(e => e.SourcePath).HasMaxLength(1024);
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Copyright).HasMaxLength(500);
            entity.Property(e => e.Sha256).HasMaxLength(64);
            entity.Property(e => e.ErrorCode).HasMaxLength(100);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.IsFileStaged).HasDefaultValue(false);

            entity.Property(e => e.Tags)
                .HasColumnType("text[]")
                .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                    (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));

            entity.Property(e => e.CollectionNames)
                .HasColumnType("text[]")
                .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                    (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));

            entity.Property(e => e.MetadataJson)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>())
                .HasColumnType(Jsonb)
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, object>>(
                    (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
                    c => JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
                    c => JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
        });

        // MetadataSchema
        modelBuilder.Entity<MetadataSchema>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("idx_metadata_schemas_name_unique");
            entity.HasIndex(e => e.Scope).HasDatabaseName("idx_metadata_schemas_scope");
            entity.HasIndex(e => e.CollectionId).HasDatabaseName("idx_metadata_schemas_collection_id");

            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Scope)
                .HasConversion(v => v.ToDbString(), v => v.ToMetadataSchemaScope())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.AssetType)
                .HasConversion(
                    v => v.HasValue ? v.Value.ToDbString() : null,
                    v => v != null ? v.ToAssetType() : null)
                .HasMaxLength(50);
            entity.Property(e => e.CreatedByUserId).HasMaxLength(255).IsRequired();

            entity.HasOne<Collection>()
                .WithMany()
                .HasForeignKey(e => e.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Fields)
                .WithOne(f => f.MetadataSchema)
                .HasForeignKey(f => f.MetadataSchemaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MetadataField
        modelBuilder.Entity<MetadataField>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MetadataSchemaId, e.Key }).IsUnique().HasDatabaseName("idx_metadata_fields_schema_key_unique");
            entity.HasIndex(e => new { e.MetadataSchemaId, e.SortOrder }).HasDatabaseName("idx_metadata_fields_schema_sort");

            entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Label).HasMaxLength(255).IsRequired();
            entity.Property(e => e.LabelSv).HasMaxLength(255);
            entity.Property(e => e.Type)
                .HasConversion(v => v.ToDbString(), v => v.ToMetadataFieldType())
                .HasMaxLength(50).IsRequired();
            entity.Property(e => e.PatternRegex).HasMaxLength(500);
            entity.Property(e => e.NumericMin).HasColumnType("numeric");
            entity.Property(e => e.NumericMax).HasColumnType("numeric");

            entity.Property(e => e.SelectOptions)
                .HasColumnType("text[]")
                .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                    (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()));

            entity.HasOne(e => e.Taxonomy)
                .WithMany()
                .HasForeignKey(e => e.TaxonomyId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Taxonomy
        modelBuilder.Entity<Taxonomy>(entity =>
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
        });

        // TaxonomyTerm
        modelBuilder.Entity<TaxonomyTerm>(entity =>
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
        });

        // AssetMetadataValue
        modelBuilder.Entity<AssetMetadataValue>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MetadataFieldId, e.AssetId }).HasDatabaseName("idx_asset_metadata_values_field_asset");
            entity.HasIndex(e => e.AssetId).HasDatabaseName("idx_asset_metadata_values_asset");
            entity.HasIndex(e => e.ValueTaxonomyTermId)
                .HasFilter("\"ValueTaxonomyTermId\" IS NOT NULL")
                .HasDatabaseName("idx_asset_metadata_values_taxonomy_term");

            entity.Property(e => e.ValueText).HasMaxLength(4000);
            entity.Property(e => e.ValueNumeric).HasColumnType("numeric");

            entity.HasOne(e => e.Asset)
                .WithMany()
                .HasForeignKey(e => e.AssetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.MetadataField)
                .WithMany()
                .HasForeignKey(e => e.MetadataFieldId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ValueTaxonomyTerm)
                .WithMany()
                .HasForeignKey(e => e.ValueTaxonomyTermId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // SavedSearch
        modelBuilder.Entity<SavedSearch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OwnerUserId).HasDatabaseName("idx_saved_searches_owner");
            entity.HasIndex(e => new { e.OwnerUserId, e.Name }).IsUnique().HasDatabaseName("idx_saved_searches_owner_name_unique");

            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.OwnerUserId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.RequestJson).HasColumnType(Jsonb).IsRequired();
            entity.Property(e => e.Notify)
                .HasConversion(v => v.ToDbString(), v => v.ToSavedSearchNotifyCadence())
                .HasMaxLength(50).IsRequired();
        });
    }
}
