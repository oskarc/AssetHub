using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public sealed class AssetVersionRepository(
    AssetHubDbContext db,
    ILogger<AssetVersionRepository> logger) : IAssetVersionRepository
{
    public async Task<List<AssetVersion>> GetByAssetIdAsync(Guid assetId, CancellationToken ct = default)
    {
        return await db.AssetVersions
            .AsNoTracking()
            .Where(v => v.AssetId == assetId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);
    }

    public async Task<AssetVersion?> GetAsync(Guid assetId, int versionNumber, CancellationToken ct = default)
    {
        return await db.AssetVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.AssetId == assetId && v.VersionNumber == versionNumber, ct);
    }

    public async Task<AssetVersion> CreateAsync(AssetVersion version, CancellationToken ct = default)
    {
        if (version.Id == Guid.Empty)
            version.Id = Guid.NewGuid();
        if (version.CreatedAt == default)
            version.CreatedAt = DateTime.UtcNow;

        db.AssetVersions.Add(version);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Captured AssetVersion {VersionId} (asset {AssetId} v{VersionNumber})",
            version.Id, version.AssetId, version.VersionNumber);
        return version;
    }

    public async Task DeleteAsync(Guid versionId, CancellationToken ct = default)
    {
        var deleted = await db.AssetVersions
            .Where(v => v.Id == versionId)
            .ExecuteDeleteAsync(ct);
        if (deleted > 0)
            logger.LogInformation("Pruned AssetVersion {VersionId}", versionId);
    }
}
