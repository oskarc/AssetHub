using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Persists audit events to the database using a dedicated DbContext
/// to avoid flushing uncommitted changes from sibling operations.
/// </summary>
public class AuditService(
    IDbContextFactory<AssetHubDbContext> dbContextFactory,
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

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
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
