using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public sealed class SavedSearchRepository(
    AssetHubDbContext db,
    ILogger<SavedSearchRepository> logger) : ISavedSearchRepository
{
    public async Task<List<SavedSearch>> GetByOwnerAsync(string ownerUserId, CancellationToken ct = default)
    {
        return await db.SavedSearches
            .AsNoTracking()
            .Where(s => s.OwnerUserId == ownerUserId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<SavedSearch?> GetByIdAsync(Guid id, string ownerUserId, CancellationToken ct = default)
    {
        return await db.SavedSearches
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == ownerUserId, ct);
    }

    public async Task<bool> ExistsByNameAsync(string ownerUserId, string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = db.SavedSearches.Where(s => s.OwnerUserId == ownerUserId && s.Name == name);
        if (excludeId.HasValue)
            query = query.Where(s => s.Id != excludeId.Value);
        return await query.AnyAsync(ct);
    }

    public async Task<SavedSearch> CreateAsync(SavedSearch savedSearch, CancellationToken ct = default)
    {
        if (savedSearch.Id == Guid.Empty)
            savedSearch.Id = Guid.NewGuid();
        if (savedSearch.CreatedAt == default)
            savedSearch.CreatedAt = DateTime.UtcNow;

        db.SavedSearches.Add(savedSearch);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("User {UserId} created saved search {Id} '{Name}'",
            savedSearch.OwnerUserId, savedSearch.Id, savedSearch.Name);
        return savedSearch;
    }

    public async Task<SavedSearch> UpdateAsync(SavedSearch savedSearch, CancellationToken ct = default)
    {
        db.SavedSearches.Update(savedSearch);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("User {UserId} updated saved search {Id}",
            savedSearch.OwnerUserId, savedSearch.Id);
        return savedSearch;
    }

    public async Task DeleteAsync(Guid id, string ownerUserId, CancellationToken ct = default)
    {
        var deleted = await db.SavedSearches
            .Where(s => s.Id == id && s.OwnerUserId == ownerUserId)
            .ExecuteDeleteAsync(ct);
        if (deleted > 0)
            logger.LogInformation("User {UserId} deleted saved search {Id}", ownerUserId, id);
    }

    public async Task<List<SavedSearch>> GetWithNotificationsEnabledAsync(CancellationToken ct = default)
    {
        return await db.SavedSearches
            .AsNoTracking()
            .Where(s => s.Notify != SavedSearchNotifyCadence.None)
            .OrderBy(s => s.LastRunAt == null ? DateTime.MinValue : s.LastRunAt)
            .ToListAsync(ct);
    }

    public async Task MarkRunAsync(Guid id, DateTime runAt, Guid? highestSeenAssetId, CancellationToken ct = default)
    {
        if (highestSeenAssetId is Guid newHighest)
        {
            await db.SavedSearches
                .Where(s => s.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.LastRunAt, runAt)
                    .SetProperty(s => s.LastHighestSeenAssetId, (Guid?)newHighest), ct);
        }
        else
        {
            await db.SavedSearches
                .Where(s => s.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.LastRunAt, runAt), ct);
        }
    }
}
