using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Handles asset query operations: search, retrieval, and collection membership queries.
/// 
/// This interface extracts the query-related methods from IAssetService to provide
/// a focused contract for read operations, following the Interface Segregation Principle.
/// 
/// Consumers that only need to read asset data should depend on this interface
/// rather than the full IAssetService.
/// </summary>
public interface IAssetQueryService
{
    /// <summary>
    /// Admin-only listing by status.
    /// </summary>
    Task<ServiceResult<List<AssetResponseDto>>> GetAssetsByStatusAsync(
        string status, int skip, int take, CancellationToken ct);

    /// <summary>
    /// Search all accessible assets with filters (respects per-user authorization).
    /// </summary>
    Task<ServiceResult<AllAssetsListResponse>> GetAllAssetsAsync(
        string? query, string? type, Guid? collectionId,
        string sortBy, int skip, int take, CancellationToken ct);

    /// <summary>
    /// Get a single asset by ID (checks access via collections).
    /// </summary>
    Task<ServiceResult<AssetResponseDto>> GetAssetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Search assets within a specific collection.
    /// </summary>
    Task<ServiceResult<AssetListResponse>> GetAssetsByCollectionAsync(
        Guid collectionId, string? query, string? type,
        string sortBy, int skip, int take, CancellationToken ct);

    /// <summary>
    /// Get deletion context for UI (collection count, can delete permanently).
    /// </summary>
    Task<ServiceResult<AssetDeletionContextDto>> GetDeletionContextAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Get all collections an asset belongs to.
    /// </summary>
    Task<ServiceResult<IEnumerable<AssetCollectionDto>>> GetAssetCollectionsAsync(
        Guid id, CancellationToken ct);

    /// <summary>
    /// Get a presigned download/preview URL for the requested rendition size.
    /// </summary>
    Task<ServiceResult<string>> GetRenditionUrlAsync(
        Guid id, string size, bool forceDownload, CancellationToken ct);
}
