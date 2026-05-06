namespace AssetHub.Domain.Entities;

/// <summary>
/// One row per watermarked download — recorded the moment a watermarked file leaves the
/// system (T5-WMK-01). The row's <see cref="Id"/> IS the opaque <c>download_token</c>
/// embedded into the recipient-fingerprint layer; resolving a leaked file means looking
/// up this row by token, decrypting the recipient PII, and returning attribution.
///
/// PII columns (<see cref="EncryptedRecipientUserId"/>, <see cref="EncryptedRecipientEmail"/>)
/// are encrypted at rest via <c>IDataProtectionProvider</c> with purpose
/// "WatermarkRecipientProtector". The matching <c>*Hash</c> columns are HMAC-SHA256
/// of the same plaintext using the same key family — indexable for analytics queries
/// (e.g. "downloads by user X") without ever exposing the plaintext.
///
/// The verify endpoint is the only code path that calls Decrypt. The
/// <c>watermark.verified</c> audit row records <c>(assetId, downloadToken)</c> only —
/// resolved PII is returned to the admin in the HTTP response and never written to the
/// audit log.
/// </summary>
public class WatermarkDownload
{
    /// <summary>Primary key. Also the opaque <c>download_token</c> embedded in the watermark.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The asset whose bytes were served. Deliberately a scalar Guid, not a navigation
    /// property — forensic rows outlive their target asset by design (the verify
    /// endpoint returns the historical AssetId even after a hard delete so an admin
    /// can still resolve "this asset USED to be ours" attribution).
    /// </summary>
    public Guid AssetId { get; set; }

    /// <summary>
    /// DataProtection-encrypted Keycloak sub of the internal user who downloaded.
    /// Null for share-recipient downloads.
    /// </summary>
    public byte[]? EncryptedRecipientUserId { get; set; }

    /// <summary>
    /// DataProtection-encrypted email of the share recipient.
    /// Null for internal-user downloads or anonymous shares.
    /// </summary>
    public byte[]? EncryptedRecipientEmail { get; set; }

    /// <summary>HMAC-SHA256 of the plaintext recipient user id. Indexable for analytics.</summary>
    public byte[]? RecipientUserIdHash { get; set; }

    /// <summary>HMAC-SHA256 of the plaintext recipient email. Indexable for analytics.</summary>
    public byte[]? RecipientEmailHash { get; set; }

    /// <summary>
    /// The share that served this download, or null for internal-app downloads.
    /// Same scalar-not-navigation reasoning as <see cref="AssetId"/>.
    /// </summary>
    public Guid? ShareId { get; set; }

    /// <summary>"raw" | "medium" | "thumb" | "zip" | "share". Per ZIP, one row per asset.</summary>
    public string DownloadPath { get; set; } = string.Empty;

    public DateTime DownloadedAt { get; set; }

    /// <summary>
    /// Algorithm version embedded with the token. Lets future rotations remain
    /// backward-compatible — old rows verify with old algorithm; new rows with new.
    /// v1 ships a single algorithm.
    /// </summary>
    public string AlgorithmVersion { get; set; } = "v1";

    /// <summary>
    /// HMAC over the embedded token (key from Data Protection). Verified at extraction
    /// time so an attacker can't fabricate a row in the DB by guessing a token format.
    /// </summary>
    public byte[] PayloadHmac { get; set; } = [];
}
