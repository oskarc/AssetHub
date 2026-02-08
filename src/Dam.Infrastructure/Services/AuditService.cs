using Dam.Application.Services;
using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

namespace Dam.Infrastructure.Services;

/// <summary>
/// Persists audit events to the database.
/// </summary>
public class AuditService(AssetHubDbContext dbContext) : IAuditService
{
    public async Task LogAsync(
        string eventType,
        string targetType,
        Guid? targetId,
        string? actorUserId,
        Dictionary<string, object>? details = null,
        HttpContext? httpContext = null,
        CancellationToken ct = default)
    {
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

        dbContext.AuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync(ct);
    }
}
