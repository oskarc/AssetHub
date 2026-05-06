using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

/// <summary>
/// EF + HybridCache implementation of <see cref="IWatermarkDownloadRepository"/>
/// (T5-WMK-01). The by-token lookup (verify-endpoint hot path) is cached;
/// the analytics queries are uncached.
/// </summary>
public sealed class WatermarkDownloadRepository(
    DbContextProvider provider,
    HybridCache cache,
    ILogger<WatermarkDownloadRepository> logger) : IWatermarkDownloadRepository
{
    /// <inheritdoc />
    public async Task AddAsync(WatermarkDownload download, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(download);

        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;

        if (download.Id == Guid.Empty)
        {
            download.Id = Guid.NewGuid();
        }
        if (download.DownloadedAt == default)
        {
            download.DownloadedAt = DateTime.UtcNow;
        }

        db.WatermarkDownloads.Add(download);
        await db.SaveChangesAsync(ct);

        logger.LogDebug(
            "Persisted WatermarkDownload {DownloadToken} for asset {AssetId} via {DownloadPath}",
            download.Id, download.AssetId, download.DownloadPath);
    }

    /// <inheritdoc />
    public async Task<WatermarkDownload?> GetByTokenAsync(Guid downloadToken, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.WatermarkDownload(downloadToken),
            async (cancellationToken) =>
            {
                await using var lease = await provider.AcquireAsync(cancellationToken);
                var db = lease.Db;
                return await db.WatermarkDownloads
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.Id == downloadToken, cancellationToken);
            },
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.WatermarkDownloadTtl,
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            },
            tags: [CacheKeys.Tags.WatermarkDownloadTag(downloadToken)],
            cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WatermarkDownload>> GetRecentForAssetAsync(
        Guid assetId, int take, CancellationToken ct)
    {
        if (take < 1) return [];

        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.WatermarkDownloads
            .AsNoTracking()
            .Where(w => w.AssetId == assetId)
            .OrderByDescending(w => w.DownloadedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WatermarkDownload>> GetByRecipientUserHashAsync(
        byte[] recipientUserIdHash, int take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recipientUserIdHash);
        if (take < 1) return [];

        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.WatermarkDownloads
            .AsNoTracking()
            .Where(w => w.RecipientUserIdHash != null
                     && w.RecipientUserIdHash.SequenceEqual(recipientUserIdHash))
            .OrderByDescending(w => w.DownloadedAt)
            .Take(take)
            .ToListAsync(ct);
    }
}
