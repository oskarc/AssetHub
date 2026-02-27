using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Service for querying audit events with filtering and pagination.
/// Separate from IAuditService (write) for single-responsibility compliance.
/// </summary>
public interface IAuditQueryService
{
    /// <summary>
    /// Gets audit events with pagination and optional filters.
    /// </summary>
    /// <param name="request">Query parameters including pagination cursor and filters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated audit events with resolved actor/target names.</returns>
    Task<ServiceResult<AuditQueryResponse>> GetAuditEventsAsync(AuditQueryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gets recent audit events (simple take-based query for backward compatibility).
    /// </summary>
    /// <param name="take">Maximum number of events to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of audit events with resolved actor/target names.</returns>
    Task<ServiceResult<List<AuditEventDto>>> GetRecentAuditEventsAsync(int take = 200, CancellationToken ct = default);
}
