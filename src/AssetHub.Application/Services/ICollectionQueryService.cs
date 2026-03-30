using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Read-only collection queries.
/// </summary>
public interface ICollectionQueryService
{
    /// <summary>Get all collections accessible to the current user.</summary>
    Task<ServiceResult<List<CollectionResponseDto>>> GetRootCollectionsAsync(CancellationToken ct);

    /// <summary>Get a single collection by ID.</summary>
    Task<ServiceResult<CollectionResponseDto>> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Get deletion context (asset count + orphan count) for a collection.</summary>
    Task<ServiceResult<CollectionDeletionContextDto>> GetDeletionContextAsync(Guid id, CancellationToken ct);
}
