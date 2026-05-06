namespace AssetHub.Application.Services.Watermarking;

/// <summary>
/// Low-level steganographic encoder (T5-WMK-01). Two methods, one per layer; the
/// <see cref="IWatermarkService"/> composes them according to the no-gap rule, and
/// <c>EmbedAssetFingerprintHandler</c> uses only the asset-layer for the background sweep.
///
/// v1 implementation: DCT-domain LSB modulation. Algorithm version is opaque to callers —
/// the embedder reports its identifier via <see cref="AlgorithmVersion"/> and the
/// service stamps that into every persisted <c>WatermarkDownload</c> row.
/// </summary>
public interface IWatermarkEmbedder
{
    /// <summary>Algorithm-version identifier stamped into rows this embedder produces.</summary>
    string AlgorithmVersion { get; }

    /// <summary>
    /// Embed the asset-fingerprint layer into a copy of the input image and return it.
    /// Used by the background sweep (writing back to MinIO) and on-the-fly when the
    /// stored renditions don't yet carry the fingerprint.
    /// </summary>
    Task<Stream> EmbedAssetFingerprintAsync(
        Stream input,
        AssetFingerprintPayload payload,
        CancellationToken ct);

    /// <summary>
    /// Embed the recipient-fingerprint layer into a copy of the input image and return it.
    /// Used per-download.
    /// </summary>
    Task<Stream> EmbedRecipientFingerprintAsync(
        Stream input,
        RecipientFingerprintPayload payload,
        CancellationToken ct);
}

/// <summary>Payload embedded by the asset-fingerprint layer. Opaque to the embedder.</summary>
public sealed record AssetFingerprintPayload(string AssetWatermarkToken, string AlgorithmVersion);

/// <summary>Payload embedded by the recipient-fingerprint layer. Opaque to the embedder.</summary>
public sealed record RecipientFingerprintPayload(Guid DownloadToken, string AlgorithmVersion, byte[] PayloadHmac);
