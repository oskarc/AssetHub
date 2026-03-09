using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Command operations for assets: update, delete, collection membership.
/// For queries, see <see cref="IAssetQueryService"/>.
/// For uploads, see <see cref="IAssetUploadService"/>.
/// </summary>
public interface IAssetService
{
    /// <summary>Update asset metadata (title, description, tags).</summary>
    Task<ServiceResult<AssetResponseDto>> UpdateAsync(
        Guid id, UpdateAssetDto dto, CancellationToken ct);

    /// <summary>Delete or unlink an asset. When fromCollectionId is set the asset is removed
    /// from that collection (and auto-deleted when orphaned). Otherwise a full permanent delete is performed.</summary>
    Task<ServiceResult> DeleteAsync(
        Guid id, Guid? fromCollectionId, CancellationToken ct);

    /// <summary>Add an asset to an additional collection.</summary>
    Task<ServiceResult<AssetAddedToCollectionResponse>> AddToCollectionAsync(
        Guid assetId, Guid collectionId, CancellationToken ct);

    /// <summary>Remove an asset from a collection (auto-deletes if orphaned).</summary>
    Task<ServiceResult> RemoveFromCollectionAsync(
        Guid assetId, Guid collectionId, CancellationToken ct);

    /// <summary>Bulk delete or unlink multiple assets. Delegates to <see cref="DeleteAsync"/> per asset.</summary>
    Task<ServiceResult<BulkDeleteAssetsResponse>> BulkDeleteAsync(
        BulkDeleteAssetsRequest request, CancellationToken ct);
}
