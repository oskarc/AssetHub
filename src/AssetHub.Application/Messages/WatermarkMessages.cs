namespace AssetHub.Application.Messages;

// ── Commands ─────────────────────────────────────────────────────────────

/// <summary>
/// Background-sweep command — embeds the asset-fingerprint layer into a single asset's
/// stored MinIO renditions (original / medium / thumb). Idempotent: handler reads the
/// asset's current state, regenerates the fingerprint, replaces the MinIO objects, and
/// stamps <c>AssetWatermarkAppliedAt</c>. Re-enqueued whenever <c>AssetWatermarkAppliedAt</c>
/// is null or older than <c>UpdatedAt</c> (T5-WMK-01).
/// </summary>
public record EmbedAssetFingerprintCommand
{
    public Guid AssetId { get; init; }
}

// RecipientWatermarkCommand (async-202 fallback for very large originals) is
// deferred to a follow-up — staging in-progress watermarked bytes for a polling
// client requires a transient-storage layer (MinIO temp object with TTL or
// equivalent) that v1 does not ship. Sync watermarking is the only path for now;
// images above WatermarkSettings.SyncSizeLimitMP are watermarked synchronously
// with a Warning log. Tracked in FOLLOW-UPS as "T5-WMK-01 — async-202 for very
// large originals".
