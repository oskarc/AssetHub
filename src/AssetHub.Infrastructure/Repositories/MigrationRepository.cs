using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public sealed class MigrationRepository(
    AssetHubDbContext dbContext,
    ILogger<MigrationRepository> logger) : IMigrationRepository
{
    public async Task<Migration?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.Migrations
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<Migration?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.Migrations
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<List<Migration>> ListAsync(int skip, int take, CancellationToken ct = default)
    {
        return await dbContext.Migrations
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        return await dbContext.Migrations.CountAsync(ct);
    }

    public async Task<Migration> CreateAsync(Migration migration, CancellationToken ct = default)
    {
        dbContext.Migrations.Add(migration);
        await dbContext.SaveChangesAsync(ct);
        return migration;
    }

    public async Task UpdateAsync(Migration migration, CancellationToken ct = default)
    {
        dbContext.Migrations.Update(migration);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var migration = await dbContext.Migrations
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (migration is not null)
        {
            dbContext.Migrations.Remove(migration);
            await dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task AddItemsAsync(IEnumerable<MigrationItem> items, CancellationToken ct = default)
    {
        dbContext.MigrationItems.AddRange(items);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<List<MigrationItem>> GetItemsAsync(
        Guid migrationId, string? statusFilter, int skip, int take, CancellationToken ct = default)
    {
        var query = dbContext.MigrationItems
            .AsNoTracking()
            .Where(i => i.MigrationId == migrationId);

        if (!string.IsNullOrEmpty(statusFilter))
        {
            var status = statusFilter.ToMigrationItemStatus();
            query = query.Where(i => i.Status == status);
        }

        return await query
            .OrderBy(i => i.RowNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountItemsAsync(Guid migrationId, string? statusFilter, CancellationToken ct = default)
    {
        var query = dbContext.MigrationItems
            .Where(i => i.MigrationId == migrationId);

        if (!string.IsNullOrEmpty(statusFilter))
        {
            var status = statusFilter.ToMigrationItemStatus();
            query = query.Where(i => i.Status == status);
        }

        return await query.CountAsync(ct);
    }

    public async Task<MigrationItem?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default)
    {
        return await dbContext.MigrationItems
            .FirstOrDefaultAsync(i => i.Id == itemId, ct);
    }

    public async Task UpdateItemAsync(MigrationItem item, CancellationToken ct = default)
    {
        dbContext.MigrationItems.Update(item);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<List<MigrationItem>> GetPendingItemsAsync(Guid migrationId, CancellationToken ct = default)
    {
        return await dbContext.MigrationItems
            .Where(i => i.MigrationId == migrationId && i.Status == MigrationItemStatus.Pending)
            .OrderBy(i => i.RowNumber)
            .ToListAsync(ct);
    }

    public async Task<List<MigrationItem>> GetFailedItemsAsync(Guid migrationId, CancellationToken ct = default)
    {
        return await dbContext.MigrationItems
            .Where(i => i.MigrationId == migrationId && i.Status == MigrationItemStatus.Failed)
            .OrderBy(i => i.RowNumber)
            .ToListAsync(ct);
    }

    public async Task<MigrationItemCounts> GetItemCountsAsync(Guid migrationId, CancellationToken ct = default)
    {
        var items = dbContext.MigrationItems.Where(i => i.MigrationId == migrationId);

        var counts = await items
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var staged = await items.CountAsync(i => i.IsFileStaged, ct);
        var stagedPending = await items.CountAsync(i => i.IsFileStaged && i.Status == MigrationItemStatus.Pending, ct);

        var total = counts.Sum(c => c.Count);
        var pending = counts.FirstOrDefault(c => c.Status == MigrationItemStatus.Pending)?.Count ?? 0;
        var processing = counts.FirstOrDefault(c => c.Status == MigrationItemStatus.Processing)?.Count ?? 0;
        var succeeded = counts.FirstOrDefault(c => c.Status == MigrationItemStatus.Succeeded)?.Count ?? 0;
        var failed = counts.FirstOrDefault(c => c.Status == MigrationItemStatus.Failed)?.Count ?? 0;
        var skipped = counts.FirstOrDefault(c => c.Status == MigrationItemStatus.Skipped)?.Count ?? 0;

        return new MigrationItemCounts(total, pending, processing, succeeded, failed, skipped, staged, stagedPending);
    }

    public async Task RemoveAllItemsAsync(Guid migrationId, CancellationToken ct = default)
    {
        await dbContext.MigrationItems
            .Where(i => i.MigrationId == migrationId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> MarkItemsStagedAsync(Guid migrationId, IEnumerable<string> fileNames, CancellationToken ct = default)
    {
        var nameSet = fileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = await dbContext.MigrationItems
            .Where(i => i.MigrationId == migrationId && !i.IsFileStaged)
            .ToListAsync(ct);

        var matched = items.Where(i => nameSet.Contains(i.FileName)).ToList();

        foreach (var item in matched)
        {
            item.IsFileStaged = true;
        }

        await dbContext.SaveChangesAsync(ct);
        return matched.Count;
    }

    public async Task<int> DeleteByStatusAsync(IReadOnlyList<MigrationStatus> statuses, CancellationToken ct = default)
    {
        var query = dbContext.Migrations.Where(m => statuses.Contains(m.Status));

        // Delete items first (cascade may not cover ExecuteDeleteAsync)
        var migrationIds = await query.Select(m => m.Id).ToListAsync(ct);
        if (migrationIds.Count == 0)
            return 0;

        await dbContext.MigrationItems
            .Where(i => migrationIds.Contains(i.MigrationId))
            .ExecuteDeleteAsync(ct);

        var deleted = await dbContext.Migrations
            .Where(m => migrationIds.Contains(m.Id))
            .ExecuteDeleteAsync(ct);

        return deleted;
    }
}
