using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Raw database queries that feed the dashboard aggregation.
/// Encapsulates all direct persistence access so that <see cref="IDashboardService"/>
/// depends only on typed interfaces rather than the raw <c>DbContext</c>.
/// </summary>
public interface IDashboardQueryService
{
    /// <summary>
    /// Resolves the highest ACL role granted to <paramref name="userId"/> across
    /// all collections. Returns <c>"viewer"</c> when the user has no ACL entries.
    /// </summary>
    Task<string> GetHighestRoleAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Returns the most recent asset <c>UpdatedAt</c> timestamp per collection,
    /// keyed by collection ID.
    /// </summary>
    Task<Dictionary<Guid, DateTime>> GetLatestUpdatesByCollectionAsync(
        IEnumerable<Guid> collectionIds, CancellationToken ct);

    /// <summary>
    /// Returns the most recent shares for the dashboard, optionally scoped to a
    /// single user. Scope names (asset title / collection name) are resolved
    /// inline so the caller receives ready-to-use DTOs.
    /// </summary>
    Task<List<DashboardShareDto>> GetRecentSharesAsync(
        string? userId, int take, CancellationToken ct);

    /// <summary>
    /// Returns recent audit events for the dashboard, optionally scoped to a
    /// single actor. Actor and target names are resolved inline.
    /// </summary>
    Task<List<AuditEventDto>> GetRecentActivityAsync(
        string? userId, int take, CancellationToken ct);

    /// <summary>
    /// Returns global aggregate statistics (admin-only dashboard widget).
    /// </summary>
    Task<DashboardStatsDto> GetGlobalStatsAsync(CancellationToken ct);
}
