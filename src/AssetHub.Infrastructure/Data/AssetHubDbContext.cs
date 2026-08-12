using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data.Configurations;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Data;

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
    public DbSet<AssetVersion> AssetVersions { get; set; } = null!;
    public DbSet<PersonalAccessToken> PersonalAccessTokens { get; set; } = null!;
    public DbSet<AssetWorkflowTransition> AssetWorkflowTransitions { get; set; } = null!;
    public DbSet<OrphanedObject> OrphanedObjects { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    // Entity configuration lives in one IEntityTypeConfiguration<T> class per entity under
    // Data/Configurations/ (CollectionConfiguration is the exemplar). Shared JSONB conventions and
    // value comparers are in Configurations/ModelConventions. Applied explicitly below — order
    // mirrors the historical inline-block order and does not affect the resulting model.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new CollectionConfiguration());
        modelBuilder.ApplyConfiguration(new CollectionAclConfiguration());
        modelBuilder.ApplyConfiguration(new AssetConfiguration());
        modelBuilder.ApplyConfiguration(new AssetCollectionConfiguration());
        modelBuilder.ApplyConfiguration(new ShareConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());
        modelBuilder.ApplyConfiguration(new ZipDownloadConfiguration());
        modelBuilder.ApplyConfiguration(new ExportPresetConfiguration());
        modelBuilder.ApplyConfiguration(new MigrationConfiguration());
        modelBuilder.ApplyConfiguration(new MigrationItemConfiguration());
        modelBuilder.ApplyConfiguration(new MetadataSchemaConfiguration());
        modelBuilder.ApplyConfiguration(new MetadataFieldConfiguration());
        modelBuilder.ApplyConfiguration(new TaxonomyConfiguration());
        modelBuilder.ApplyConfiguration(new TaxonomyTermConfiguration());
        modelBuilder.ApplyConfiguration(new AssetMetadataValueConfiguration());
        modelBuilder.ApplyConfiguration(new SavedSearchConfiguration());
        modelBuilder.ApplyConfiguration(new AssetVersionConfiguration());
        modelBuilder.ApplyConfiguration(new PersonalAccessTokenConfiguration());
        modelBuilder.ApplyConfiguration(new AssetWorkflowTransitionConfiguration());
        modelBuilder.ApplyConfiguration(new OrphanedObjectConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
