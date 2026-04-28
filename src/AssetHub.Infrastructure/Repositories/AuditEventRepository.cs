using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

/// <summary>
/// Read-only EF Core access to <see cref="AuditEvent"/> rows.
/// </summary>
public sealed class AuditEventRepository(DbContextProvider provider) : IAuditEventRepository
{
    /// <inheritdoc/>
    /// <remarks>
    /// Fetches <paramref name="take"/> rows after applying filters and the cursor.
    /// The total-count query is capped at <see cref="Constants.Limits.AuditCountDisplayCap"/> + 1
    /// so the UI can show "10 000+" without scanning the full table.
    /// </remarks>
    public async Task<(List<AuditEvent> Events, int TotalCount)> GetPageAsync(
        AuditQueryRequest request, int take, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var dbContext = lease.Db;
        var query = dbContext.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.EventType))
            query = query.Where(e => e.EventType == request.EventType);

        if (!string.IsNullOrWhiteSpace(request.TargetType))
            query = query.Where(e => e.TargetType == request.TargetType);

        if (!string.IsNullOrWhiteSpace(request.ActorUserId))
            query = query.Where(e => e.ActorUserId == request.ActorUserId);

        // Count is capped for performance on large datasets
        var totalCount = await query.Take(Constants.Limits.AuditCountDisplayCap + 1).CountAsync(ct);

        if (request.Cursor.HasValue)
            query = query.Where(e => e.CreatedAt < request.Cursor.Value);

        var events = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        return (events, totalCount);
    }

    public async Task<List<AuditEvent>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var dbContext = lease.Db;
        return await dbContext.AuditEvents
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var dbContext = lease.Db;
        return await dbContext.AuditEvents
            .Where(e => e.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteOlderThanBatchAsync(DateTime cutoff, int batchSize, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var dbContext = lease.Db;
        return await dbContext.AuditEvents
            .Where(e => e.CreatedAt < cutoff)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteByEventTypeOlderThanBatchAsync(
        string eventType, DateTime cutoff, int batchSize, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var dbContext = lease.Db;
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return await dbContext.AuditEvents
            .Where(e => e.EventType == eventType && e.CreatedAt < cutoff)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteOlderThanBatchExcludingTypesAsync(
        DateTime cutoff,
        IReadOnlyCollection<string> excludedEventTypes,
        int batchSize,
        CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var dbContext = lease.Db;
        if (excludedEventTypes.Count == 0)
            return await DeleteOlderThanBatchAsync(cutoff, batchSize, ct);

        return await dbContext.AuditEvents
            .Where(e => e.CreatedAt < cutoff && !excludedEventTypes.Contains(e.EventType))
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(ct);
    }
}
