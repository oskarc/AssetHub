using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Persists audit events to the database using a dedicated DbContext
/// to avoid flushing uncommitted changes from sibling operations.
/// </summary>
public class AuditService(
    IDbContextFactory<AssetHubDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor) : IAuditService
{
    public async Task LogAsync(
        string eventType,
        string targetType,
        Guid? targetId,
        string? actorUserId,
        Dictionary<string, object>? details = null,
        CancellationToken ct = default)
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
            UserAgent = httpContext?.Request.Headers.UserAgent.FirstOrDefault(),
            DetailsJson = details ?? new Dictionary<string, object>(),
            CreatedAt = DateTime.UtcNow
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        dbContext.AuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync(ct);
    }
}
