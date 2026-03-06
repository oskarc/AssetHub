using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Read-only access to persisted audit events.
/// Write access is handled by <see cref="Services.IAuditService"/>.
/// </summary>
public interface IAuditEventRepository
{
    /// <summary>
    /// Returns a filtered, cursor-paginated page of audit events.
    /// Fetches <paramref name="take"/> + 1 rows so the caller can detect HasMore.
    /// </summary>
    Task<(List<AuditEvent> Events, int TotalCount)> GetPageAsync(
        AuditQueryRequest request,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the <paramref name="take"/> most-recent audit events.
    /// </summary>
    Task<List<AuditEvent>> GetRecentAsync(int take, CancellationToken ct = default);

    /// <summary>
    /// Deletes all audit events created before <paramref name="cutoff"/>.
    /// Returns the number of rows deleted.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
