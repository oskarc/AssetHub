namespace AssetHub.Application.Services.Watermarking;

/// <summary>
/// Steganographic decoder (T5-WMK-01). Extracts each layer independently and reports
/// per-layer confidence so the verify endpoint can return rich attribution
/// (high / medium / low / no-match per layer) on cropped or re-encoded leaks.
///
/// The extractor reports format-level confidence only — the recipient layer's HMAC
/// check happens in the <see cref="IWatermarkVerifier"/> using
/// <see cref="IRecipientCryptoService"/>. The raw payload bytes are exposed here so
/// the verifier can HMAC-verify without re-running the DCT pass.
/// </summary>
public interface IWatermarkExtractor
{
    /// <summary>Algorithm-version identifier this extractor knows how to decode.</summary>
    string AlgorithmVersion { get; }

    /// <summary>
    /// Attempt to extract both layers from the input image. Always returns a result
    /// (no exceptions for benign no-match cases). The caller's verification endpoint
    /// shapes the user-facing response from per-layer confidence.
    /// </summary>
    Task<WatermarkExtractionResult> ExtractAsync(
        Stream input,
        CancellationToken ct);
}

/// <summary>Outcome of extracting both layers from a suspect file.</summary>
public sealed record WatermarkExtractionResult(
    AssetLayerExtraction Asset,
    RecipientLayerExtraction Recipient);

/// <summary>Asset-fingerprint layer extraction result.</summary>
public sealed record AssetLayerExtraction(
    string? Token,
    byte[]? RawPayload,
    WatermarkLayerConfidence Confidence);

/// <summary>Recipient-fingerprint layer extraction result. <see cref="RawPayload"/> is the 32-byte payload (token + version + HMAC) for the verifier to HMAC-check.</summary>
public sealed record RecipientLayerExtraction(
    Guid? Token,
    byte[]? RawPayload,
    WatermarkLayerConfidence Confidence);

/// <summary>
/// Per-layer extraction confidence. The extractor reports format-level confidence
/// (NoMatch / Medium); the verifier may upgrade Medium → High after HMAC verification
/// or downgrade if cross-checks fail.
/// </summary>
public enum WatermarkLayerConfidence
{
    NoMatch = 0,
    Low = 1,
    Medium = 2,
    High = 3
}
