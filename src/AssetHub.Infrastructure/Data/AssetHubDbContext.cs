using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data.Configurations;
using AssetHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Data;

/// <remarks>
/// Derives from <see cref="IdentityDbContext{TUser}"/> so the local Identity
/// provider has a user store. The Identity tables exist regardless of which
/// provider is configured — they are simply unused under Keycloak, which keeps
/// the schema stable across a provider switch.
/// </remarks>
public class AssetHubDbContext : IdentityDbContext<AppUser>, IDataProtectionKeyContext
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
    public DbSet<Migration> Migrations { get; set; } = null!;
    public DbSet<MigrationItem> MigrationItems { get; set; } = null!;
    public DbSet<AssetVersion> AssetVersions { get; set; } = null!;
    public DbSet<OrphanedObject> OrphanedObjects { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    // Entity configuration lives in one IEntityTypeConfiguration<T> class per entity under
    // Data/Configurations/ (CollectionConfiguration is the exemplar). Shared JSONB conventions and
    // value comparers are in Configurations/ModelConventions. Applied explicitly below — order
    // mirrors the historical inline-block order and does not affect the resulting model.
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new CollectionConfiguration());
        builder.ApplyConfiguration(new CollectionAclConfiguration());
        builder.ApplyConfiguration(new AssetConfiguration());
        builder.ApplyConfiguration(new AssetCollectionConfiguration());
        builder.ApplyConfiguration(new ShareConfiguration());
        builder.ApplyConfiguration(new AuditEventConfiguration());
        builder.ApplyConfiguration(new ZipDownloadConfiguration());
        builder.ApplyConfiguration(new MigrationConfiguration());
        builder.ApplyConfiguration(new MigrationItemConfiguration());
        builder.ApplyConfiguration(new AssetVersionConfiguration());
        builder.ApplyConfiguration(new OrphanedObjectConfiguration());
        builder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
