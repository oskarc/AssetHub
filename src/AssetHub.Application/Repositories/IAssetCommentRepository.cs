using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Persistence for <see cref="AssetComment"/>. All list operations accept
/// an <c>assetId</c> so callers stay honest about asset-scoped authorization
/// checks made upstream in the service.
/// </summary>
public interface IAssetCommentRepository
{
    /// <summary>
    /// List every comment (top-level + replies) for an asset, ordered by
    /// CreatedAt ASC so the UI renders in conversation order. Returns
    /// tracking-disabled entities for read paths.
    /// </summary>
    Task<List<AssetComment>> ListByAssetAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>Get a comment by id. Does not filter by asset — callers re-check.</summary>
    Task<AssetComment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<AssetComment> CreateAsync(AssetComment comment, CancellationToken ct = default);

    Task UpdateAsync(AssetComment comment, CancellationToken ct = default);

    /// <summary>Delete by id. Cascades to replies via the parent FK. Returns true if a row was removed.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
