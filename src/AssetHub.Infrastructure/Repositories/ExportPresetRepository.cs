using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public sealed class ExportPresetRepository(
    AssetHubDbContext dbContext,
    HybridCache cache,
    ILogger<ExportPresetRepository> logger) : IExportPresetRepository
{
    public async Task<ExportPreset?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.ExportPreset(id),
            async cancel => await dbContext.ExportPresets
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, cancel),
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.ExportPresetTtl,
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            },
            tags: [CacheKeys.Tags.ExportPresets],
            cancellationToken: ct);
    }

    public async Task<List<ExportPreset>> GetAllAsync(CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.ExportPresetsAll(),
            async cancel => await dbContext.ExportPresets
                .AsNoTracking()
                .OrderBy(p => p.Name)
                .ToListAsync(cancel),
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.ExportPresetTtl,
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            },
            tags: [CacheKeys.Tags.ExportPresets],
            cancellationToken: ct) ?? new List<ExportPreset>();
    }

    public async Task<List<ExportPreset>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        return await dbContext.ExportPresets
            .AsNoTracking()
            .Where(p => idList.Contains(p.Id))
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = dbContext.ExportPresets.Where(p => p.Name == name);
        if (excludeId.HasValue)
            query = query.Where(p => p.Id != excludeId.Value);
        return await query.AnyAsync(ct);
    }

    public async Task<ExportPreset> CreateAsync(ExportPreset preset, CancellationToken ct = default)
    {
        if (preset.Id == Guid.Empty)
            preset.Id = Guid.NewGuid();
        if (preset.CreatedAt == default)
            preset.CreatedAt = DateTime.UtcNow;

        dbContext.ExportPresets.Add(preset);
        await dbContext.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.ExportPresets, ct);

        logger.LogInformation("Created export preset {PresetId} with name '{Name}'", preset.Id, preset.Name);
        return preset;
    }

    public async Task<ExportPreset?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.ExportPresets
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<ExportPreset> UpdateAsync(ExportPreset preset, CancellationToken ct = default)
    {
        dbContext.ExportPresets.Update(preset);
        await dbContext.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.ExportPresets, ct);

        logger.LogInformation("Updated export preset {PresetId}", preset.Id);
        return preset;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var preset = await dbContext.ExportPresets.FindAsync([id], ct);
        if (preset is not null)
        {
            dbContext.ExportPresets.Remove(preset);
            await dbContext.SaveChangesAsync(ct);
            await cache.RemoveByTagAsync(CacheKeys.Tags.ExportPresets, ct);

            logger.LogInformation("Deleted export preset {PresetId}", id);
        }
    }
}
