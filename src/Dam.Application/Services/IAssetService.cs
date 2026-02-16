using Dam.Application.Dtos;
using Microsoft.AspNetCore.Http;

namespace Dam.Application.Services;

/// <summary>
/// Orchestrates all asset operations: CRUD, upload, collection membership, and renditions.
/// Authorization, auditing, and storage coordination belong here — not in endpoints.
/// </summary>
public interface IAssetService
{
    // ── Queries ──────────────────────────────────────────────────────────────

    /// <summary>Admin-only listing by status.</summary>
    Task<ServiceResult<List<AssetResponseDto>>> GetAssetsByStatusAsync(
        string status, int skip, int take, CancellationToken ct);

    /// <summary>Search all accessible assets with filters (respects per-user authorization).</summary>
    Task<ServiceResult<AllAssetsListResponse>> GetAllAssetsAsync(
        string? query, string? type, Guid? collectionId,
        string sortBy, int skip, int take, CancellationToken ct);

    /// <summary>Get a single asset by ID (checks access via collections).</summary>
    Task<ServiceResult<AssetResponseDto>> GetAssetAsync(Guid id, CancellationToken ct);

    /// <summary>Search assets within a specific collection.</summary>
    Task<ServiceResult<AssetListResponse>> GetAssetsByCollectionAsync(
        Guid collectionId, string? query, string? type,
        string sortBy, int skip, int take, CancellationToken ct);

    /// <summary>Get deletion context for UI (collection count, can delete permanently).</summary>
    Task<ServiceResult<AssetDeletionContextDto>> GetDeletionContextAsync(Guid id, CancellationToken ct);

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Upload a file directly through the API (small/medium files).</summary>
    Task<ServiceResult<AssetUploadResult>> UploadAsync(
        Stream fileStream, string fileName, string contentType, long fileSize,
        Guid collectionId, string title, CancellationToken ct);

    /// <summary>Update asset metadata (title, description, tags).</summary>
    Task<ServiceResult<AssetResponseDto>> UpdateAsync(
        Guid id, UpdateAssetDto dto, CancellationToken ct);

    /// <summary>Delete or unlink an asset (handles partial/full/permanent delete).</summary>
    Task<ServiceResult> DeleteAsync(
        Guid id, Guid? fromCollectionId, bool permanent, CancellationToken ct);

    // ── Presigned Upload ─────────────────────────────────────────────────────

    /// <summary>Step 1: Create asset record and return presigned PUT URL for direct browser upload.</summary>
    Task<ServiceResult<InitUploadResponse>> InitUploadAsync(
        InitUploadRequest request, CancellationToken ct);

    /// <summary>Step 2: Confirm that the browser upload completed, trigger media processing.</summary>
    Task<ServiceResult<AssetUploadResult>> ConfirmUploadAsync(Guid id, CancellationToken ct);

    // ── Multi-Collection ─────────────────────────────────────────────────────

    /// <summary>Get all collections an asset belongs to.</summary>
    Task<ServiceResult<IEnumerable<AssetCollectionDto>>> GetAssetCollectionsAsync(
        Guid id, CancellationToken ct);

    /// <summary>Add an asset to an additional collection.</summary>
    Task<ServiceResult<AssetAddedToCollectionResponse>> AddToCollectionAsync(
        Guid assetId, Guid collectionId, CancellationToken ct);

    /// <summary>Remove an asset from a collection (auto-deletes if orphaned).</summary>
    Task<ServiceResult> RemoveFromCollectionAsync(
        Guid assetId, Guid collectionId, CancellationToken ct);

    // ── Renditions ───────────────────────────────────────────────────────────

    /// <summary>Get a presigned download/preview URL for the requested rendition size.</summary>
    Task<ServiceResult<string>> GetRenditionUrlAsync(
        Guid id, string size, CancellationToken ct);
}
