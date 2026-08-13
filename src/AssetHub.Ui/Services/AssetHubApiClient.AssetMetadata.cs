using AssetHub.Application;
using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    // ─── Asset ↔ Collection membership (multi-collection) ──────────────────

    public async Task<List<AssetCollectionDto>> GetAssetCollectionsAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetQueryService.GetAssetCollectionsAsync(assetId, ct);
        return Unwrap(result, "Get asset collections").ToList();
    }

    public async Task AddAssetToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var result = await assetService.AddToCollectionAsync(assetId, collectionId, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Add asset to collection");
    }

    public async Task RemoveAssetFromCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var result = await assetService.RemoveFromCollectionAsync(assetId, collectionId, ct);
        EnsureSuccess(result, "Remove asset from collection");
    }
}
