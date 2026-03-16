using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Queries audit events with filtering, pagination, and name resolution.
/// </summary>
public class AuditQueryService(
    IAuditEventRepository auditRepo,
    IAssetRepository assetRepo,
    ICollectionRepository collectionRepo,
    IUserLookupService userLookup) : IAuditQueryService
{
    public async Task<ServiceResult<AuditQueryResponse>> GetAuditEventsAsync(AuditQueryRequest request, CancellationToken ct = default)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, Constants.Limits.MaxPageSize);

        var (events, totalCount) = await auditRepo.GetPageAsync(request, pageSize + 1, ct);

        var hasMore = events.Count > pageSize;
        if (hasMore)
            events = events.Take(pageSize).ToList();

        // Resolve names
        var items = await ResolveNamesAsync(events, ct);

        return new AuditQueryResponse
        {
            Items = items,
            TotalCount = Math.Min(totalCount, Constants.Limits.AuditCountDisplayCap),
            IsCapped = totalCount > Constants.Limits.AuditCountDisplayCap,
            NextCursor = hasMore && events.Count > 0 ? events[^1].CreatedAt : null,
            HasMore = hasMore
        };
    }

    public async Task<ServiceResult<List<AuditEventDto>>> GetRecentAuditEventsAsync(int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, Constants.Limits.MaxPageSize);
        var events = await auditRepo.GetRecentAsync(take, ct);
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

        var assetNames = await assetRepo.GetTitlesByIdsAsync(assetIds, ct);
        var collectionNames = await collectionRepo.GetNamesByIdsAsync(collectionIds, ct);

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

