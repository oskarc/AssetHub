using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Faceted search across assets. Uses the <c>search_vector</c> tsvector column on Assets
/// (populated by triggers — see migration <c>AddAssetSearchAndSavedSearch</c>) for full-text
/// matching, then runs per-facet aggregation queries against the filtered result set.
/// </summary>
public interface IAssetSearchService
{
    /// <summary>
    /// Executes the search, honouring collection-scoped RBAC. Non-admin callers only see assets
    /// in collections where they have at least Viewer access.
    /// </summary>
    Task<ServiceResult<AssetSearchResponse>> SearchAsync(AssetSearchRequest request, CancellationToken ct);
}
