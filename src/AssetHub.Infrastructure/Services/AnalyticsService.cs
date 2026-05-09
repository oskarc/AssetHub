using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Watermarking;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Read-side analytics queries (T5-ANL-01). Hydrates rollup hash-keyed
/// rows with display names (asset title, collection name) by joining
/// against the live tables. Recipient PII is never decrypted by these
/// methods — that's <see cref="RevealRecipientAsync"/>'s job, and it
/// audits the reveal under <c>analytics.exposure_revealed</c>.
/// </summary>
public sealed class AnalyticsService(
    IAnalyticsRollupRepository rollupRepo,
    DbContextProvider dbContextProvider,
    IRecipientCryptoService recipientCrypto,
    IAuditService auditService,
    ILogger<AnalyticsService> logger) : IAnalyticsService
{
    public async Task<ServiceResult<IReadOnlyList<AnalyticsAssetDownloadRowDto>>> GetTopDownloadedAssetsAsync(
        int windowDays, int take, CancellationToken ct)
    {
        var (from, to) = WindowFromToday(windowDays);
        var rows = await rollupRepo.GetTopByCountAsync(
            AnalyticsCountMetric.DownloadsByAsset, from, to, take, ct);
        if (rows.Count == 0) return new List<AnalyticsAssetDownloadRowDto>();

        // Parse the GUID rollup keys; preserve the original order so the top-N
        // ordering survives the dictionary join.
        var assetIds = rows
            .Select(r => Guid.TryParse(r.EntityId, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();

        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var assets = await lease.Db.Assets
            .IgnoreQueryFilters() // include soft-deleted; admin sees the leak history
            .AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                a.Title,
                AssetType = a.AssetType.ToDbString(),
                IsDeleted = a.DeletedAt != null
            })
            .ToDictionaryAsync(a => a.Id, ct);

        var result = rows
            .Select(r =>
            {
                if (!Guid.TryParse(r.EntityId, out var assetId))
                    return null;
                assets.TryGetValue(assetId, out var asset);
                return new AnalyticsAssetDownloadRowDto
                {
                    AssetId = assetId,
                    Title = asset?.Title,
                    AssetType = asset?.AssetType,
                    DownloadCount = r.Count,
                    IsDeleted = asset?.IsDeleted ?? true // missing row == hard-deleted
                };
            })
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
        return result;
    }

    public async Task<ServiceResult<IReadOnlyList<AnalyticsDailyPointDto>>> GetDailyDownloadCountsAsync(
        int windowDays, CancellationToken ct)
    {
        var (from, to) = WindowFromToday(windowDays);
        var rows = await rollupRepo.GetDailyTotalsAsync(
            AnalyticsCountMetric.DownloadsByAsset, from, to, ct);
        return rows
            .Select(r => new AnalyticsDailyPointDto { Date = r.Date, Count = r.Count })
            .ToList();
    }

    public async Task<ServiceResult<IReadOnlyList<AnalyticsStorageByCollectionRowDto>>> GetStorageByCollectionAsync(
        int take, CancellationToken ct)
    {
        var rows = await rollupRepo.GetLatestStorageAsync(
            AnalyticsStorageMetric.StorageByCollection, take, ct);
        if (rows.Count == 0) return new List<AnalyticsStorageByCollectionRowDto>();

        var collectionIds = rows
            .Select(r => Guid.TryParse(r.EntityId, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();

        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var names = await lease.Db.Collections
            .AsNoTracking()
            .Where(c => collectionIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return rows
            .Select(r =>
            {
                if (!Guid.TryParse(r.EntityId, out var id))
                    return null;
                names.TryGetValue(id, out var name);
                return new AnalyticsStorageByCollectionRowDto
                {
                    CollectionId = id,
                    CollectionName = name,
                    Bytes = r.Bytes,
                    AssetCount = r.AssetCount
                };
            })
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
    }

    public async Task<ServiceResult<IReadOnlyList<AnalyticsStorageByAssetTypeRowDto>>> GetStorageByAssetTypeAsync(
        CancellationToken ct)
    {
        // No top-N — there are only a handful of asset-type rows.
        var rows = await rollupRepo.GetLatestStorageAsync(
            AnalyticsStorageMetric.StorageByAssetType, take: 50, ct);
        return rows
            .Select(r => new AnalyticsStorageByAssetTypeRowDto
            {
                AssetType = r.EntityId,
                Bytes = r.Bytes,
                AssetCount = r.AssetCount
            })
            .ToList();
    }

    public async Task<ServiceResult<IReadOnlyList<AnalyticsExposureRowDto>>> GetTopRecipientsAsync(
        int windowDays, int take, CancellationToken ct)
    {
        var (from, to) = WindowFromToday(windowDays);
        var rows = await rollupRepo.GetTopByCountAsync(
            AnalyticsCountMetric.ExposureByRecipient, from, to, take, ct);
        if (rows.Count == 0) return new List<AnalyticsExposureRowDto>();

        var hashes = rows.Select(r => r.EntityId).ToList();
        var distinctCounts = await rollupRepo.GetDistinctAssetCountsByRecipientAsync(
            from, to, hashes, ct);

        // The rollup row knows the count but not whether the hash is on
        // RecipientUserIdHash or RecipientEmailHash. Resolve via WatermarkDownload
        // — we only need a single row per hash to answer.
        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var hashSet = hashes.ToHashSet();

        var kindByHash = await lease.Db.WatermarkDownloads
            .AsNoTracking()
            .Where(w => w.DownloadedAt >= fromUtc && w.DownloadedAt < toUtc)
            .Where(w =>
                (w.RecipientUserIdHash != null && hashSet.Contains(Convert.ToBase64String(w.RecipientUserIdHash))) ||
                (w.RecipientEmailHash != null && hashSet.Contains(Convert.ToBase64String(w.RecipientEmailHash))))
            .Select(w => new
            {
                Hash = w.RecipientUserIdHash != null
                    ? Convert.ToBase64String(w.RecipientUserIdHash)
                    : Convert.ToBase64String(w.RecipientEmailHash!),
                Kind = w.RecipientUserIdHash != null
                    ? AnalyticsConstants.RecipientKinds.User
                    : AnalyticsConstants.RecipientKinds.Email
            })
            .Distinct()
            .ToListAsync(ct);

        var kinds = kindByHash.ToDictionary(r => r.Hash, r => r.Kind);

        return rows
            .Select(r => new AnalyticsExposureRowDto
            {
                RecipientHash = r.EntityId,
                RecipientKind = kinds.GetValueOrDefault(r.EntityId, AnalyticsConstants.RecipientKinds.User),
                DownloadCount = r.Count,
                DistinctAssetCount = distinctCounts.GetValueOrDefault(r.EntityId, 0)
            })
            .ToList();
    }

    public async Task<ServiceResult<RevealRecipientResponseDto>> RevealRecipientAsync(
        RevealRecipientRequestDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        byte[] hashBytes;
        try
        {
            hashBytes = Convert.FromBase64String(request.RecipientHash);
        }
        catch (FormatException)
        {
            return ServiceError.BadRequest("Recipient hash is not valid base64.");
        }

        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var db = lease.Db;

        // Find the most recent WatermarkDownload row that matches this hash on
        // the requested kind. We only need the encrypted PII column — the
        // crypto service handles decryption errors as null (key rotation
        // tolerance, established by T5-WMK-01).
        var query = request.RecipientKind switch
        {
            AnalyticsConstants.RecipientKinds.User => db.WatermarkDownloads
                .AsNoTracking()
                .Where(w => w.RecipientUserIdHash != null && w.RecipientUserIdHash == hashBytes)
                .OrderByDescending(w => w.DownloadedAt)
                .Select(w => new { Encrypted = w.EncryptedRecipientUserId, Kind = AnalyticsConstants.RecipientKinds.User }),
            AnalyticsConstants.RecipientKinds.Email => db.WatermarkDownloads
                .AsNoTracking()
                .Where(w => w.RecipientEmailHash != null && w.RecipientEmailHash == hashBytes)
                .OrderByDescending(w => w.DownloadedAt)
                .Select(w => new { Encrypted = w.EncryptedRecipientEmail, Kind = AnalyticsConstants.RecipientKinds.Email }),
            _ => null
        };

        if (query is null) return ServiceError.BadRequest("recipientKind must be 'user' or 'email'.");

        var row = await query.FirstOrDefaultAsync(ct);
        if (row is null || row.Encrypted is null)
        {
            // No row for this hash → either it was purged or the hash is bogus.
            return ServiceError.NotFound("No watermark download matches this recipient hash.");
        }

        var plaintext = recipientCrypto.Decrypt(row.Encrypted);
        var decryptionFailed = plaintext is null;

        // G-12b carries forward from T5-WMK-01: never log decrypted PII. The
        // audit row knows the hash + kind + outcome so a security review can
        // see "admin revealed recipient X at time Y" without leaking the value.
        try
        {
            await auditService.LogAsync(
                eventType: AnalyticsConstants.AuditEvents.ExposureRevealed,
                targetType: AnalyticsConstants.ScopeTypes.Analytics,
                targetId: Guid.Empty,
                actorUserId: null,
                details: new Dictionary<string, object>
                {
                    ["recipient_hash"] = request.RecipientHash,
                    ["recipient_kind"] = request.RecipientKind,
                    ["decryption_failed"] = decryptionFailed
                },
                ct: ct);
        }
        catch (Exception ex)
        {
            // Audit failure must not block the admin's reveal — log and continue.
            logger.LogWarning(ex, "Failed to write analytics.exposure_revealed audit row");
        }

        return new RevealRecipientResponseDto
        {
            RecipientUserId = !decryptionFailed && row.Kind == AnalyticsConstants.RecipientKinds.User ? plaintext : null,
            RecipientEmail = !decryptionFailed && row.Kind == AnalyticsConstants.RecipientKinds.Email ? plaintext : null,
            DecryptionFailed = decryptionFailed
        };
    }

    private static (DateOnly From, DateOnly To) WindowFromToday(int windowDays)
    {
        // Inclusive [from, to]. "today" rounds to UTC because rollup keys are UTC dates.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return (today.AddDays(-Math.Max(1, windowDays - 1)), today);
    }
}
