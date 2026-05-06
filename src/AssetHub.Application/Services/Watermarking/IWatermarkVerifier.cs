using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services.Watermarking;

/// <summary>
/// Admin-only watermark verify path (T5-WMK-01). Extracts both watermark layers from an
/// uploaded suspect file and resolves the embedded download token to a
/// <c>WatermarkDownload</c> row. Decrypts recipient PII via
/// <see cref="IRecipientCryptoService"/> for return to the admin only — the
/// <c>watermark.verified</c> audit row records <c>(assetId, downloadToken)</c> only.
/// </summary>
public interface IWatermarkVerifier
{
    /// <summary>
    /// Extract watermark layers from <paramref name="input"/> and resolve attribution.
    /// On a clean miss, returns a result with <c>Matched = false</c> rather than an error —
    /// "no match" is a legitimate outcome of verification. Returns
    /// <c>WMK_UNSUPPORTED_FORMAT</c> / <c>WMK_TOO_LARGE</c> for invalid uploads, and
    /// <c>WMK_DECRYPTION_FAILED</c> when key-rotation drift makes a matching row unreadable.
    /// </summary>
    Task<ServiceResult<WatermarkVerificationResultDto>> VerifyAsync(
        Stream input,
        string contentType,
        long sizeBytes,
        CancellationToken ct);
}
