using Dam.Domain.Entities;

namespace Dam.Application.Services;

/// <summary>
/// Service for recording audit events for important operations.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Records an audit event.
    /// </summary>
    /// <param name="eventType">The type of event (e.g., "asset.created", "collection.deleted", "share.revoked").</param>
    /// <param name="targetType">The entity type affected (e.g., "asset", "collection", "share", "user").</param>
    /// <param name="targetId">The ID of the affected entity, if applicable.</param>
    /// <param name="actorUserId">The user who performed the action.</param>
    /// <param name="details">Optional structured details about the event.</param>
    /// <param name="httpContext">Optional HTTP context for capturing IP and User-Agent.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LogAsync(
        string eventType,
        string targetType,
        Guid? targetId,
        string? actorUserId,
        Dictionary<string, object>? details = null,
        Microsoft.AspNetCore.Http.HttpContext? httpContext = null,
        CancellationToken ct = default);
}
