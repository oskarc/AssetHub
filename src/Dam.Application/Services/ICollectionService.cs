using Dam.Application.Dtos;

namespace Dam.Application.Services;

/// <summary>
/// Orchestrates collection CRUD, child navigation, and bulk download.
/// Authorization and auditing belong here — not in endpoints.
/// </summary>
public interface ICollectionService
{
    /// <summary>Get root-level (entry-point) collections accessible to the current user.</summary>
    Task<ServiceResult<List<CollectionResponseDto>>> GetRootCollectionsAsync(CancellationToken ct);

    /// <summary>Get a single collection by ID.</summary>
    Task<ServiceResult<CollectionResponseDto>> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Create a new collection (root or sub-collection).</summary>
    Task<ServiceResult<CollectionResponseDto>> CreateAsync(CreateCollectionDto dto, CancellationToken ct);

    /// <summary>Update collection name/description.</summary>
    Task<ServiceResult<MessageResponse>> UpdateAsync(Guid id, UpdateCollectionDto dto, CancellationToken ct);

    /// <summary>Delete a collection and handle its assets (orphan cleanup).</summary>
    Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Get child collections of a parent.</summary>
    Task<ServiceResult<List<CollectionResponseDto>>> GetChildrenAsync(Guid parentId, CancellationToken ct);

    /// <summary>Enqueue a background ZIP build for all assets in a collection.</summary>
    Task<ServiceResult<ZipDownloadEnqueuedResponse>> DownloadAllAssetsAsync(Guid id, CancellationToken ct);
}
