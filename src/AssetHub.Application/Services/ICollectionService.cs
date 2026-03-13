using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Collection commands: create, update, delete, and download.
/// Authorization and auditing belong here — not in endpoints.
/// </summary>
public interface ICollectionService
{
    /// <summary>Create a new collection.</summary>
    Task<ServiceResult<CollectionResponseDto>> CreateAsync(CreateCollectionDto dto, CancellationToken ct);

    /// <summary>Update collection name/description.</summary>
    Task<ServiceResult<MessageResponse>> UpdateAsync(Guid id, UpdateCollectionDto dto, CancellationToken ct);

    /// <summary>Delete a collection and handle its assets (orphan cleanup).</summary>
    Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Enqueue a background ZIP build for all assets in a collection.</summary>
    Task<ServiceResult<ZipDownloadEnqueuedResponse>> DownloadAllAssetsAsync(Guid id, CancellationToken ct);
}
