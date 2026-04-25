using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Standard DI shape: repos + audit + webhook publisher + scoped CurrentUser + 2 IOptions + logger. Bundling them into a holder would obscure intent.")]
public sealed class AssetTrashService(
    IAssetRepository assetRepo,
    IAssetDeletionService deletionService,
    IAuditService audit,
    IWebhookEventPublisher webhooks,
    CurrentUser currentUser,
    IOptions<AssetLifecycleSettings> lifecycleSettings,
    IOptions<MinIOSettings> minioSettings,
    ILogger<AssetTrashService> logger) : IAssetTrashService
{
    private readonly string _bucket = minioSettings.Value.BucketName;
    private readonly TimeSpan _retention = TimeSpan.FromDays(lifecycleSettings.Value.TrashRetentionDays);

    public async Task<ServiceResult<TrashListResponse>> GetAsync(int skip, int take, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var (assets, total) = await assetRepo.GetTrashAsync(skip, take, ct);
        var items = assets.Select(a => new TrashedAssetDto
        {
            Id = a.Id,
            Title = a.Title,
            AssetType = a.AssetType.ToDbString(),
            SizeBytes = a.SizeBytes,
            ThumbObjectKey = a.ThumbObjectKey,
            PosterObjectKey = a.PosterObjectKey,
            DeletedAt = a.DeletedAt!.Value,
            DeletedByUserId = a.DeletedByUserId,
            ExpiresAt = a.DeletedAt!.Value + _retention
        }).ToList();

        return new TrashListResponse { Items = items, TotalCount = total };
    }

    public async Task<ServiceResult> RestoreAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var asset = await assetRepo.GetByIdIncludingDeletedAsync(id, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found");
        if (asset.DeletedAt is null) return ServiceError.BadRequest("Asset is not in Trash");

        await deletionService.RestoreAsync(asset, ct);
        await audit.LogAsync("asset.restored", Constants.ScopeTypes.Asset, id, currentUser.UserId,
            new() { ["title"] = asset.Title }, ct);
        await webhooks.PublishAsync(WebhookEvents.AssetRestored, new
        {
            assetId = id,
            title = asset.Title,
            restoredByUserId = currentUser.UserId,
            restoredAt = DateTime.UtcNow
        }, ct);
        logger.LogInformation("Admin {UserId} restored asset {AssetId} from Trash", currentUser.UserId, id);
        return ServiceResult.Success;
    }

    public async Task<ServiceResult> PurgeAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var asset = await assetRepo.GetByIdIncludingDeletedAsync(id, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found");
        if (asset.DeletedAt is null) return ServiceError.BadRequest("Asset must be in Trash before it can be purged");

        await deletionService.PurgeAsync(asset, _bucket, ct);
        await audit.LogAsync("asset.purged", Constants.ScopeTypes.Asset, id, currentUser.UserId,
            new() { ["title"] = asset.Title }, ct);
        logger.LogInformation("Admin {UserId} purged asset {AssetId} from Trash", currentUser.UserId, id);
        return ServiceResult.Success;
    }

    public async Task<ServiceResult<EmptyTrashResponse>> EmptyAsync(CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        // Pull all trashed assets in pages so a million-row trash doesn't blow up the request.
        // 200/page is a reasonable middle ground — large enough that empty-trash on a typical
        // library is one or two iterations, small enough that a stuck purge fails fast.
        const int pageSize = 200;
        int purged = 0;
        int failed = 0;

        while (true)
        {
            var (assets, total) = await assetRepo.GetTrashAsync(0, pageSize, ct);
            if (assets.Count == 0) break;

            foreach (var asset in assets)
            {
                try
                {
                    await deletionService.PurgeAsync(asset, _bucket, ct);
                    purged++;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogWarning(ex, "Failed to purge asset {AssetId} during EmptyAsync", asset.Id);
                }
            }

            // If everything in the batch failed, break to avoid an infinite loop on the same rows.
            if (purged == 0 && failed == assets.Count) break;
            if (assets.Count < pageSize) break;
            // Otherwise the next iteration picks up the next page (purged rows are now gone).
            _ = total;
        }

        await audit.LogAsync("asset.trash_emptied", Constants.ScopeTypes.Asset, null, currentUser.UserId,
            new() { ["purged"] = purged, ["failed"] = failed }, ct);
        logger.LogInformation("Admin {UserId} emptied Trash: {Purged} purged, {Failed} failed",
            currentUser.UserId, purged, failed);

        return new EmptyTrashResponse { Purged = purged, Failed = failed };
    }
}
