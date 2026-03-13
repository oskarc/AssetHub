using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Admin-only bulk collection operations.
/// </summary>
public interface ICollectionAdminService
{
    /// <summary>Delete multiple collections at once (admin only).</summary>
    Task<ServiceResult<BulkDeleteCollectionsResponse>> BulkDeleteAsync(List<Guid> collectionIds, bool deleteAssets, CancellationToken ct);

    /// <summary>Set access on multiple collections at once (admin only).</summary>
    Task<ServiceResult<BulkSetCollectionAccessResponse>> BulkSetAccessAsync(BulkSetCollectionAccessRequest request, CancellationToken ct);
}
