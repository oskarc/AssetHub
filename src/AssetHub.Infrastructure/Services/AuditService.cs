using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Persists audit events to the request-scoped <see cref="AssetHubDbContext"/>
/// so that, when a caller wraps the action + audit in
/// <see cref="IUnitOfWork.ExecuteAsync"/>, both rows commit atomically (A-4).
/// </summary>
/// <remarks>
/// Outside a UnitOfWork the call still works — SaveChanges runs immediately
/// and the audit lands as its own transaction. The cost of sharing the
/// DbContext is that pending tracker entries on the same context will flush
/// alongside the audit row; services should avoid <c>Add</c> / <c>Update</c>
/// in a half-built state before calling <see cref="LogAsync"/>.
/// </remarks>
public class AuditService(
    AssetHubDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditService> logger) : IAuditService
{
    public async Task LogAsync(
        string eventType,
        string targetType,
        Guid? targetId,
        string? actorUserId,
        Dictionary<string, object>? details = null,
        CancellationToken ct = default)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;

            var auditEvent = new AuditEvent
            {
                Id = Guid.NewGuid(),
                EventType = eventType,
                TargetType = targetType,
                TargetId = targetId,
                ActorUserId = actorUserId,
                IP = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.FirstOrDefault() is { } ua
                    ? ua[..Math.Min(ua.Length, 512)]
                    : null,
                DetailsJson = details ?? new Dictionary<string, object>(),
                CreatedAt = DateTime.UtcNow
            };

            dbContext.AuditEvents.Add(auditEvent);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist audit event {EventType} for {TargetType}/{TargetId}",
                eventType, targetType, targetId);
        }
    }
}
