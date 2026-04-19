using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

public sealed class AssetVersionService(
    IAssetVersionRepository versionRepo,
    IAssetRepository assetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    ICollectionAuthorizationService authService,
    IMinIOAdapter minioAdapter,
    IAuditService audit,
    CurrentUser currentUser,
    IOptions<MinIOSettings> minioSettings,
    ILogger<AssetVersionService> logger) : IAssetVersionService
{
    private readonly string _bucket = minioSettings.Value.BucketName;

    public async Task<ServiceResult<List<AssetVersionDto>>> GetForAssetAsync(Guid assetId, CancellationToken ct)
    {
        var asset = await assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found");

        if (!await CanAccessAsync(assetId, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        var versions = await versionRepo.GetByAssetIdAsync(assetId, ct);
        return versions.Select(v => ToDto(v, asset.CurrentVersionNumber)).ToList();
    }

    public async Task<ServiceResult<AssetVersionDto>> RestoreAsync(Guid assetId, int versionNumber, CancellationToken ct)
    {
        var asset = await assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found");

        if (!await CanAccessAsync(assetId, RoleHierarchy.Roles.Contributor, ct))
            return ServiceError.Forbidden();

        var target = await versionRepo.GetAsync(assetId, versionNumber, ct);
        if (target is null) return ServiceError.NotFound($"Version {versionNumber} not found");

        // Capture the asset's current state as a new version BEFORE overwriting it. Restore
        // is itself reversible — restoring v1 from v3 produces v4 (= snapshot of v3) and
        // then writes the v1 snapshot onto the asset row.
        var snapshotOfCurrent = new AssetVersion
        {
            AssetId = asset.Id,
            VersionNumber = asset.CurrentVersionNumber + 1,
            OriginalObjectKey = asset.OriginalObjectKey,
            ThumbObjectKey = asset.ThumbObjectKey,
            MediumObjectKey = asset.MediumObjectKey,
            PosterObjectKey = asset.PosterObjectKey,
            SizeBytes = asset.SizeBytes,
            ContentType = asset.ContentType,
            Sha256 = asset.Sha256 ?? string.Empty,
            EditDocument = asset.EditDocument,
            MetadataSnapshot = new Dictionary<string, object>(asset.MetadataJson),
            CreatedByUserId = currentUser.UserId,
            ChangeNote = $"Auto-snapshot before restoring v{versionNumber}"
        };
        await versionRepo.CreateAsync(snapshotOfCurrent, ct);

        // Overwrite the asset row with the chosen version's snapshot — including object keys,
        // so the live asset now points at the historical bytes (no MinIO copy needed).
        asset.OriginalObjectKey = target.OriginalObjectKey;
        asset.ThumbObjectKey = target.ThumbObjectKey;
        asset.MediumObjectKey = target.MediumObjectKey;
        asset.PosterObjectKey = target.PosterObjectKey;
        asset.SizeBytes = target.SizeBytes;
        asset.ContentType = target.ContentType;
        asset.Sha256 = target.Sha256;
        asset.EditDocument = target.EditDocument;
        asset.MetadataJson = new Dictionary<string, object>(target.MetadataSnapshot);
        asset.CurrentVersionNumber = snapshotOfCurrent.VersionNumber;
        asset.UpdatedAt = DateTime.UtcNow;
        await assetRepo.UpdateAsync(asset, ct);

        await audit.LogAsync("asset.version_restored", Constants.ScopeTypes.Asset, assetId, currentUser.UserId,
            new() { ["title"] = asset.Title, ["restoredFrom"] = versionNumber, ["newVersion"] = snapshotOfCurrent.VersionNumber }, ct);
        logger.LogInformation("User {UserId} restored asset {AssetId} from v{Restored} (now v{Current})",
            currentUser.UserId, assetId, versionNumber, snapshotOfCurrent.VersionNumber);

        return ToDto(snapshotOfCurrent, asset.CurrentVersionNumber);
    }

    public async Task<ServiceResult> PruneAsync(Guid assetId, int versionNumber, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden("Only administrators can prune versions");

        var asset = await assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found");
        if (versionNumber == asset.CurrentVersionNumber)
            return ServiceError.BadRequest("Cannot prune the current version — restore another version first");

        var target = await versionRepo.GetAsync(assetId, versionNumber, ct);
        if (target is null) return ServiceError.NotFound($"Version {versionNumber} not found");

        // The version's MinIO keys may be referenced by the asset's current row (if the user
        // restored from this version earlier without further mutation) or by other version
        // rows (siblings restored from the same source). Don't delete a key that's still in
        // use by the live asset; the cascade-on-purge later will clean it up if it ever
        // becomes truly orphaned.
        var keysSafeToDelete = new List<string>();
        foreach (var key in CollectKeys(target))
        {
            if (key is null) continue;
            if (KeyIsLive(asset, key)) continue;
            keysSafeToDelete.Add(key);
        }

        foreach (var key in keysSafeToDelete)
        {
            try { await minioAdapter.DeleteAsync(_bucket, key, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to delete object {Key} during version prune", key); }
        }

        await versionRepo.DeleteAsync(target.Id, ct);
        await audit.LogAsync("asset.version_pruned", Constants.ScopeTypes.Asset, assetId, currentUser.UserId,
            new() { ["title"] = asset.Title, ["versionNumber"] = versionNumber }, ct);

        return ServiceResult.Success;
    }

    private async Task<bool> CanAccessAsync(Guid assetId, string requiredRole, CancellationToken ct)
    {
        if (currentUser.IsSystemAdmin) return true;
        var collectionIds = await assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        if (collectionIds.Count == 0) return false;
        var accessible = await authService.FilterAccessibleAsync(currentUser.UserId, collectionIds, requiredRole, ct);
        return accessible.Count > 0;
    }

    private static IEnumerable<string?> CollectKeys(AssetVersion v) => new[]
    {
        v.OriginalObjectKey, v.ThumbObjectKey, v.MediumObjectKey, v.PosterObjectKey
    };

    private static bool KeyIsLive(Asset asset, string key) =>
        asset.OriginalObjectKey == key
        || asset.ThumbObjectKey == key
        || asset.MediumObjectKey == key
        || asset.PosterObjectKey == key;

    private static AssetVersionDto ToDto(AssetVersion v, int currentVersionNumber) => new()
    {
        Id = v.Id,
        AssetId = v.AssetId,
        VersionNumber = v.VersionNumber,
        ThumbObjectKey = v.ThumbObjectKey,
        SizeBytes = v.SizeBytes,
        ContentType = v.ContentType,
        Sha256 = v.Sha256,
        CreatedByUserId = v.CreatedByUserId,
        CreatedAt = v.CreatedAt,
        ChangeNote = v.ChangeNote,
        IsCurrent = v.VersionNumber == currentVersionNumber
    };
}
