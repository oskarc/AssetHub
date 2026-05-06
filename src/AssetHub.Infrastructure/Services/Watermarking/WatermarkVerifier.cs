using System.Diagnostics.CodeAnalysis;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
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
/// Default <see cref="IWatermarkVerifier"/> (T5-WMK-01). Runs the extractor against
/// an uploaded suspect image, HMAC-verifies the recipient layer, resolves the asset
/// layer to a current asset row, decrypts recipient PII via
/// <see cref="IRecipientCryptoService"/>, and emits a <c>watermark.verified</c>
/// audit event that records <c>(assetId, downloadToken)</c> only — never the
/// resolved PII.
/// </summary>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for the admin verify path: extraction, HMAC check, asset / download lookup, decryption, audit.")]
public sealed class WatermarkVerifier(
    DbContextProvider dbContextProvider,
    IWatermarkDownloadRepository downloadRepository,
    IWatermarkExtractor extractor,
    IRecipientCryptoService cryptoService,
    IAuditService auditService,
    IOptions<WatermarkSettings> settings,
    ILogger<WatermarkVerifier> logger) : IWatermarkVerifier
{
    private readonly WatermarkSettings _settings = settings.Value;

    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    /// <inheritdoc />
    public async Task<ServiceResult<WatermarkVerificationResultDto>> VerifyAsync(
        Stream input, string contentType, long sizeBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!SupportedContentTypes.Contains(contentType))
        {
            return ServiceError.BadRequest(
                $"Unsupported file type '{contentType}'. v1 supports JPEG, PNG, and WebP.")
                with { Code = WatermarkConstants.ErrorCodes.UnsupportedFormat };
        }

        if (sizeBytes > _settings.VerifyMaxBytes)
        {
            return ServiceError.BadRequest(
                $"File too large ({sizeBytes} bytes). Max accepted at the verify endpoint is {_settings.VerifyMaxBytes} bytes.")
                with { Code = WatermarkConstants.ErrorCodes.TooLarge };
        }

        WatermarkExtractionResult extraction;
        try
        {
            extraction = await extractor.ExtractAsync(input, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Watermark extraction threw");
            return ServiceError.Server("Watermark extraction failed.")
                with { Code = WatermarkConstants.ErrorCodes.ExtractionFailed };
        }

        // Resolve the asset layer: look up an Asset row by AssetWatermarkToken.
        var assetResolution = await ResolveAssetLayerAsync(extraction.Asset, ct);

        // Resolve the recipient layer: HMAC-check, then fetch WatermarkDownload, decrypt PII.
        var recipientResolution = await ResolveRecipientLayerAsync(extraction.Recipient, ct);

        var matched = assetResolution.Confidence != WatermarkLayerConfidence.NoMatch
                   || recipientResolution.Confidence != WatermarkLayerConfidence.NoMatch;

        var result = new WatermarkVerificationResultDto
        {
            Matched = matched,
            AssetId = assetResolution.AssetId,
            DownloadToken = recipientResolution.DownloadToken,
            RecipientUserId = recipientResolution.RecipientUserId,
            RecipientEmail = recipientResolution.RecipientEmail,
            ShareId = recipientResolution.ShareId,
            DownloadedAt = recipientResolution.DownloadedAt,
            DownloadPath = recipientResolution.DownloadPath,
            AssetLayerConfidence = assetResolution.Confidence.ToWireString(),
            RecipientLayerConfidence = recipientResolution.Confidence.ToWireString(),
            Reason = BuildReason(assetResolution, recipientResolution)
        };

        // Audit — never include resolved PII.
        await auditService.LogAsync(
            eventType: WatermarkConstants.AuditEvents.Verified,
            targetType: Constants.ScopeTypes.Asset,
            targetId: assetResolution.AssetId,
            actorUserId: null, // Endpoint-side records the admin actor in details if needed.
            details: new Dictionary<string, object>
            {
                ["matched"] = matched,
                ["download_token"] = recipientResolution.DownloadToken is null
                    ? string.Empty
                    : recipientResolution.DownloadToken.Value.ToString("D"),
                ["asset_layer_confidence"] = result.AssetLayerConfidence,
                ["recipient_layer_confidence"] = result.RecipientLayerConfidence
            },
            ct: ct);

        return result;
    }

    private async Task<AssetResolution> ResolveAssetLayerAsync(
        AssetLayerExtraction extraction, CancellationToken ct)
    {
        if (extraction.Token is null || extraction.Confidence == WatermarkLayerConfidence.NoMatch)
        {
            return new AssetResolution(null, WatermarkLayerConfidence.NoMatch);
        }

        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var db = lease.Db;

        // Search current + soft-deleted (admin verify is read-only and historical).
        var asset = await db.Assets
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.AssetWatermarkToken == extraction.Token, ct);

        if (asset is null)
        {
            // Token decoded but no current asset matches — bits may be from a hard-deleted
            // asset. Confidence stays Medium (extraction succeeded) but no AssetId returned.
            return new AssetResolution(null, WatermarkLayerConfidence.Medium);
        }

        return new AssetResolution(asset.Id, WatermarkLayerConfidence.High);
    }

    private async Task<RecipientResolution> ResolveRecipientLayerAsync(
        RecipientLayerExtraction extraction, CancellationToken ct)
    {
        if (extraction.Token is null || extraction.RawPayload is null
            || extraction.Confidence == WatermarkLayerConfidence.NoMatch)
        {
            return RecipientResolution.NoMatch;
        }

        // HMAC-check the embedded payload bytes against IRecipientCryptoService.
        var (embeddedToken, embeddedVersion, embeddedHmac) = WatermarkPayloadCodec.Unpack(extraction.RawPayload);
        var expectedHmacInput = $"watermark.recipient:v{embeddedVersion:X}:{embeddedToken:D}";

        // The HMAC input for v1 is "watermark.recipient:v1:{guid:D}". Build with the
        // embedder-side format so verification matches embedding.
        var algorithmTag = embeddedVersion == WatermarkPayloadCodec.AlgorithmVersionV1
            ? WatermarkConstants.AlgorithmVersions.V1
            : $"v{embeddedVersion:X}";
        var hmacInput = $"watermark.recipient:{algorithmTag}:{embeddedToken:D}";

        var fullExpectedHmac = cryptoService.Hash(hmacInput);
        var hmacMatches = fullExpectedHmac.AsSpan(0, 15).SequenceEqual(embeddedHmac);

        if (!hmacMatches)
        {
            // Bits decoded but HMAC failed — corruption or tampering.
            logger.LogInformation(
                "Watermark recipient-layer HMAC mismatch for token {DownloadToken}",
                extraction.Token);
            return new RecipientResolution(
                DownloadToken: extraction.Token,
                RecipientUserId: null,
                RecipientEmail: null,
                ShareId: null,
                DownloadedAt: null,
                DownloadPath: null,
                Confidence: WatermarkLayerConfidence.Low);
        }

        // HMAC valid; fetch the row and decrypt PII for the admin response.
        var row = await downloadRepository.GetByTokenAsync(extraction.Token.Value, ct);
        if (row is null)
        {
            // Token verifies but no row — possibly purged or a bit-flip happened to land
            // on a valid-but-fictional token. Surface as Medium confidence.
            return new RecipientResolution(
                DownloadToken: extraction.Token,
                RecipientUserId: null,
                RecipientEmail: null,
                ShareId: null,
                DownloadedAt: null,
                DownloadPath: null,
                Confidence: WatermarkLayerConfidence.Medium);
        }

        return new RecipientResolution(
            DownloadToken: row.Id,
            RecipientUserId: row.EncryptedRecipientUserId is null
                ? null
                : cryptoService.Decrypt(row.EncryptedRecipientUserId),
            RecipientEmail: row.EncryptedRecipientEmail is null
                ? null
                : cryptoService.Decrypt(row.EncryptedRecipientEmail),
            ShareId: row.ShareId,
            DownloadedAt: row.DownloadedAt,
            DownloadPath: row.DownloadPath,
            Confidence: WatermarkLayerConfidence.High);
    }

    private static string? BuildReason(AssetResolution asset, RecipientResolution recipient)
    {
        if (asset.Confidence == WatermarkLayerConfidence.Medium && asset.AssetId is null)
        {
            return "Asset-fingerprint extracted but no current asset matches — the asset may have been purged.";
        }
        if (recipient.Confidence == WatermarkLayerConfidence.Low)
        {
            return "Recipient-fingerprint extracted but tamper-check failed — the file may have been modified after download.";
        }
        if (recipient.Confidence == WatermarkLayerConfidence.Medium && recipient.RecipientUserId is null && recipient.RecipientEmail is null)
        {
            return "Recipient-fingerprint extracted but the matching download record could not be resolved.";
        }
        return null;
    }

    private sealed record AssetResolution(Guid? AssetId, WatermarkLayerConfidence Confidence);

    private sealed record RecipientResolution(
        Guid? DownloadToken,
        string? RecipientUserId,
        string? RecipientEmail,
        Guid? ShareId,
        DateTime? DownloadedAt,
        string? DownloadPath,
        WatermarkLayerConfidence Confidence)
    {
        public static readonly RecipientResolution NoMatch =
            new(null, null, null, null, null, null, WatermarkLayerConfidence.NoMatch);
    }
}

internal static class WatermarkLayerConfidenceExtensions
{
    public static string ToWireString(this WatermarkLayerConfidence confidence) => confidence switch
    {
        WatermarkLayerConfidence.NoMatch => "no_match",
        WatermarkLayerConfidence.Low => "low",
        WatermarkLayerConfidence.Medium => "medium",
        WatermarkLayerConfidence.High => "high",
        _ => "no_match"
    };
}
