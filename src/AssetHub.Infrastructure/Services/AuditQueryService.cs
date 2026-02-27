using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Queries audit events with filtering, pagination, and name resolution.
/// </summary>
public class AuditQueryService(
    AssetHubDbContext db,
    IUserLookupService userLookup) : IAuditQueryService
{
    private const int MaxPageSize = 200;

    public async Task<ServiceResult<AuditQueryResponse>> GetAuditEventsAsync(AuditQueryRequest request, CancellationToken ct = default)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var query = db.AuditEvents.AsNoTracking();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(request.EventType))
            query = query.Where(e => e.EventType == request.EventType);

        if (!string.IsNullOrWhiteSpace(request.TargetType))
            query = query.Where(e => e.TargetType == request.TargetType);

        if (!string.IsNullOrWhiteSpace(request.ActorUserId))
            query = query.Where(e => e.ActorUserId == request.ActorUserId);

        // Get total count (capped for performance on large datasets)
        var totalCount = await query.Take(10001).CountAsync(ct);

        // Apply cursor-based pagination
        if (request.Cursor.HasValue)
            query = query.Where(e => e.CreatedAt < request.Cursor.Value);

        // Fetch one extra to determine HasMore
        var events = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = events.Count > pageSize;
        if (hasMore)
            events = events.Take(pageSize).ToList();

        // Resolve names
        var items = await ResolveNamesAsync(events, ct);

        return new AuditQueryResponse
        {
            Items = items,
            TotalCount = Math.Min(totalCount, 10000), // Cap displayed count
            NextCursor = hasMore && events.Count > 0 ? events[^1].CreatedAt : null,
            HasMore = hasMore
        };
    }

    public async Task<ServiceResult<List<AuditEventDto>>> GetRecentAuditEventsAsync(int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxPageSize);

        var events = await db.AuditEvents
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        return await ResolveNamesAsync(events, ct);
    }

    private async Task<List<AuditEventDto>> ResolveNamesAsync(
        List<Domain.Entities.AuditEvent> events,
        CancellationToken ct)
    {
        // Resolve actor usernames in batch
        var actorIds = events
            .Select(e => e.ActorUserId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct();
        var actorNames = await userLookup.GetUserNamesAsync(actorIds!, ct);

        // Resolve target names by type
        var assetIds = events
            .Where(e => e.TargetId.HasValue && e.TargetType == Constants.ScopeTypes.Asset)
            .Select(e => e.TargetId!.Value)
            .Distinct()
            .ToList();

        var collectionIds = events
            .Where(e => e.TargetId.HasValue && e.TargetType == Constants.ScopeTypes.Collection)
            .Select(e => e.TargetId!.Value)
            .Distinct()
            .ToList();

        var assetNames = await db.Assets
            .AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Title })
            .ToDictionaryAsync(a => a.Id, a => a.Title, ct);

        var collectionNames = await db.Collections
            .AsNoTracking()
            .Where(c => collectionIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return events.Select(e => new AuditEventDto
        {
            EventType = e.EventType,
            TargetType = e.TargetType,
            TargetId = e.TargetId,
            TargetName = ResolveTargetName(e.TargetType, e.TargetId, assetNames, collectionNames),
            ActorUserId = e.ActorUserId,
            ActorUserName = e.ActorUserId != null ? actorNames.GetValueOrDefault(e.ActorUserId) : null,
            CreatedAt = e.CreatedAt,
            Details = e.DetailsJson
        }).ToList();
    }

    private static string? ResolveTargetName(
        string targetType,
        Guid? targetId,
        Dictionary<Guid, string> assetNames,
        Dictionary<Guid, string> collectionNames)
    {
        if (!targetId.HasValue) return null;
        return targetType switch
        {
            Constants.ScopeTypes.Asset => assetNames.GetValueOrDefault(targetId.Value),
            Constants.ScopeTypes.Collection => collectionNames.GetValueOrDefault(targetId.Value),
            _ => null
        };
    }
}
