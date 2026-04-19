using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface IAssetVersionRepository
{
    /// <summary>List all versions for an asset, ordered by VersionNumber descending (newest first).</summary>
    Task<List<AssetVersion>> GetByAssetIdAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>Lookup a specific version. Returns null if absent.</summary>
    Task<AssetVersion?> GetAsync(Guid assetId, int versionNumber, CancellationToken ct = default);

    /// <summary>Persist a new version row. Caller is responsible for setting VersionNumber.</summary>
    Task<AssetVersion> CreateAsync(AssetVersion version, CancellationToken ct = default);

    /// <summary>Hard-delete a single version row by id.</summary>
    Task DeleteAsync(Guid versionId, CancellationToken ct = default);
}
