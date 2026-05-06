using System.Diagnostics.CodeAnalysis;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Watermarking;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services.Watermarking;

/// <summary>
/// Default <see cref="IWatermarkService"/> (T5-WMK-01). Composes the precedence
/// resolution (Share → Asset → Collection), the two-layer embedder calls (per the
/// no-gap rule), <see cref="WatermarkDownload"/> persistence, and the
/// <c>watermark.applied_on_download</c> audit event.
/// </summary>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for the watermark download path: precedence query, embedding, encryption, persistence, and audit are all inherent responsibilities of this service.")]
public sealed class WatermarkService(
    DbContextProvider dbContextProvider,
    IAssetRepository assetRepository,
    IWatermarkDownloadRepository downloadRepository,
    IWatermarkEmbedder embedder,
    IRecipientCryptoService cryptoService,
    IUnitOfWork unitOfWork,
    IAuditService auditService,
    IOptions<WatermarkSettings> settings,
    ILogger<WatermarkService> logger) : IWatermarkService
{
    private readonly WatermarkSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<ServiceResult<WatermarkedDownloadResult>> WatermarkForDownloadAsync(
        Stream input, WatermarkContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        var (effectiveOn, asset) = await ResolveEffectiveAsync(context.AssetId, context.ShareId, ct);
        if (asset is null)
        {
            return ServiceError.NotFound($"Asset {context.AssetId} not found.");
        }

        if (!effectiveOn)
        {
            // Watermarking is effectively off for this download path. Stream through unchanged.
            return new WatermarkedDownloadResult
            {
                Output = input,
                DownloadToken = Guid.Empty,
                WasWatermarked = false
            };
        }

        // No-gap rule: ensure the asset has a fingerprint token even if the background
        // sweep hasn't run yet. The on-the-fly path will embed both layers.
        if (asset.AssetWatermarkToken is null)
        {
            asset.AssetWatermarkToken = Guid.NewGuid().ToString("D");
            await assetRepository.UpdateAsync(asset, ct);
        }

        // Generate the per-download token and tamper-detection HMAC.
        var downloadToken = Guid.NewGuid();
        var hmacFull = ComputeRecipientHmac(downloadToken);
        // Embed only the first 15 bytes to fit the 32-byte payload budget; full HMAC
        // is persisted on the row for redundant verification.
        var hmacEmbedded = hmacFull.AsSpan(0, 15).ToArray();

        // Embed both layers when the stored renditions don't yet carry the asset
        // fingerprint; otherwise only the recipient layer.
        Stream current = input;
        try
        {
            if (asset.AssetWatermarkAppliedAt is null)
            {
                var assetPayload = new AssetFingerprintPayload(
                    AssetWatermarkToken: asset.AssetWatermarkToken,
                    AlgorithmVersion: embedder.AlgorithmVersion);
                current = await embedder.EmbedAssetFingerprintAsync(current, assetPayload, ct);
            }

            var recipientPayload = new RecipientFingerprintPayload(
                DownloadToken: downloadToken,
                AlgorithmVersion: embedder.AlgorithmVersion,
                PayloadHmac: hmacEmbedded);
            current = await embedder.EmbedRecipientFingerprintAsync(current, recipientPayload, ct);
        }
        catch (InvalidOperationException ex)
        {
            // Image-too-small or other deterministic embed failure — surface as a service error.
            logger.LogWarning(ex,
                "Watermark embedding failed for asset {AssetId} download {DownloadToken}",
                context.AssetId, downloadToken);
            return ServiceError.BadRequest(ex.Message);
        }

        // Persist the WatermarkDownload row + audit in one unit.
        var row = BuildRow(context, downloadToken, hmacFull);

        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            await downloadRepository.AddAsync(row, innerCt);
            await auditService.LogAsync(
                eventType: WatermarkConstants.AuditEvents.AppliedOnDownload,
                targetType: Constants.ScopeTypes.Asset,
                targetId: context.AssetId,
                actorUserId: context.RecipientUserId,
                details: new Dictionary<string, object>
                {
                    ["download_token"] = downloadToken,
                    ["share_id"] = context.ShareId is null ? string.Empty : context.ShareId.Value.ToString("D"),
                    ["download_path"] = context.DownloadPath,
                    ["algorithm_version"] = embedder.AlgorithmVersion,
                    ["asset_layer_embedded_inline"] = asset.AssetWatermarkAppliedAt is null
                },
                ct: innerCt);
        }, ct);

        return new WatermarkedDownloadResult
        {
            Output = current,
            DownloadToken = downloadToken,
            WasWatermarked = true
        };
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> IsWatermarkingEffectiveAsync(
        Guid assetId, Guid? shareId, CancellationToken ct)
    {
        var (effectiveOn, asset) = await ResolveEffectiveAsync(assetId, shareId, ct);
        if (asset is null) return ServiceError.NotFound($"Asset {assetId} not found.");
        return effectiveOn;
    }

    /// <inheritdoc />
    public async Task<ServiceResult> SetCollectionWatermarkAsync(
        Guid collectionId, bool enabled, CancellationToken ct)
    {
        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var db = lease.Db;
        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == collectionId, ct);
        if (collection is null) return ServiceError.NotFound($"Collection {collectionId} not found.");

        if (collection.WatermarkEnabled == enabled)
        {
            return ServiceResult.Success;
        }

        collection.WatermarkEnabled = enabled;

        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            await db.SaveChangesAsync(innerCt);
            await auditService.LogAsync(
                eventType: WatermarkConstants.AuditEvents.CollectionEnabled,
                targetType: Constants.ScopeTypes.Collection,
                targetId: collectionId,
                actorUserId: null,
                details: new Dictionary<string, object>
                {
                    ["enabled"] = enabled
                },
                ct: innerCt);
        }, ct);

        return ServiceResult.Success;
    }

    /// <inheritdoc />
    public async Task<ServiceResult> SetAssetWatermarkOverrideAsync(
        Guid assetId, bool? @override, CancellationToken ct)
    {
        var asset = await assetRepository.GetByIdAsync(assetId, ct);
        if (asset is null) return ServiceError.NotFound($"Asset {assetId} not found.");

        if (asset.WatermarkOverride == @override)
        {
            return ServiceResult.Success;
        }

        var fromValue = asset.WatermarkOverride;
        asset.WatermarkOverride = @override;

        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            await assetRepository.UpdateAsync(asset, innerCt);
            await auditService.LogAsync(
                eventType: WatermarkConstants.AuditEvents.AssetOverridden,
                targetType: Constants.ScopeTypes.Asset,
                targetId: assetId,
                actorUserId: null,
                details: new Dictionary<string, object>
                {
                    ["from"] = fromValue?.ToString() ?? "inherit",
                    ["to"] = @override?.ToString() ?? "inherit"
                },
                ct: innerCt);
        }, ct);

        return ServiceResult.Success;
    }

    /// <inheritdoc />
    public async Task<ServiceResult> SetShareWatermarkOverrideAsync(
        Guid shareId, bool? @override, CancellationToken ct)
    {
        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var db = lease.Db;
        var share = await db.Shares.FirstOrDefaultAsync(s => s.Id == shareId, ct);
        if (share is null) return ServiceError.NotFound($"Share {shareId} not found.");

        if (share.WatermarkOverride == @override)
        {
            return ServiceResult.Success;
        }

        var fromValue = share.WatermarkOverride;
        share.WatermarkOverride = @override;

        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            await db.SaveChangesAsync(innerCt);
            await auditService.LogAsync(
                eventType: WatermarkConstants.AuditEvents.ShareOverridden,
                targetType: Constants.ScopeTypes.Share,
                targetId: shareId,
                actorUserId: null,
                details: new Dictionary<string, object>
                {
                    ["from"] = fromValue?.ToString() ?? "inherit",
                    ["to"] = @override?.ToString() ?? "inherit"
                },
                ct: innerCt);
        }, ct);

        return ServiceResult.Success;
    }

    /// <summary>
    /// Resolve the effective watermark setting for an (asset, share) pair via the
    /// Share → Asset → Collection precedence. Returns the asset row alongside so the
    /// caller doesn't re-fetch.
    /// </summary>
    private async Task<(bool EffectiveOn, Asset? Asset)> ResolveEffectiveAsync(
        Guid assetId, Guid? shareId, CancellationToken ct)
    {
        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var db = lease.Db;

        var asset = await db.Assets
            .Include(a => a.AssetCollections)
                .ThenInclude(ac => ac.Collection)
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);

        if (asset is null) return (false, null);

        if (shareId is { } shareGuid)
        {
            var share = await db.Shares
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == shareGuid, ct);
            if (share?.WatermarkOverride is bool shareOverride)
            {
                return (shareOverride, asset);
            }
        }

        if (asset.WatermarkOverride is bool assetOverride)
        {
            return (assetOverride, asset);
        }

        // Collection-level: ANY collection the asset is in with WatermarkEnabled = true
        // wins. Multi-collection assets thus get the union of their collections' settings.
        var collectionOn = asset.AssetCollections
            .Any(ac => ac.Collection is { WatermarkEnabled: true });
        return (collectionOn, asset);
    }

    private byte[] ComputeRecipientHmac(Guid downloadToken)
    {
        // Domain-separated HMAC input: includes algorithm version so a future v2
        // can't be replayed against v1 verification, and prefix so it can't collide
        // with the recipient-PII hash use of the same key.
        var input = $"watermark.recipient:{embedder.AlgorithmVersion}:{downloadToken:D}";
        return cryptoService.Hash(input);
    }

    private WatermarkDownload BuildRow(WatermarkContext context, Guid downloadToken, byte[] hmacFull)
    {
        return new WatermarkDownload
        {
            Id = downloadToken,
            AssetId = context.AssetId,
            EncryptedRecipientUserId = context.RecipientUserId is null
                ? null
                : cryptoService.Encrypt(context.RecipientUserId),
            EncryptedRecipientEmail = context.RecipientEmail is null
                ? null
                : cryptoService.Encrypt(context.RecipientEmail),
            RecipientUserIdHash = context.RecipientUserId is null
                ? null
                : cryptoService.Hash(context.RecipientUserId),
            RecipientEmailHash = context.RecipientEmail is null
                ? null
                : cryptoService.Hash(context.RecipientEmail),
            ShareId = context.ShareId,
            DownloadPath = context.DownloadPath,
            DownloadedAt = DateTime.UtcNow,
            AlgorithmVersion = embedder.AlgorithmVersion,
            PayloadHmac = hmacFull
        };
    }
}
