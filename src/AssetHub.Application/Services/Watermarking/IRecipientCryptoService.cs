namespace AssetHub.Application.Services.Watermarking;

/// <summary>
/// Encrypts / decrypts watermark recipient PII at rest (T5-WMK-01). Wraps
/// <c>IDataProtectionProvider</c> with purpose <c>"WatermarkRecipientProtector"</c>
/// for symmetric encryption, plus a keyed-HMAC for the indexable hash columns.
/// The verify endpoint is the only code path that calls <see cref="Decrypt"/>.
/// </summary>
public interface IRecipientCryptoService
{
    /// <summary>Encrypts plaintext for storage in <c>EncryptedRecipient*</c> columns.</summary>
    byte[] Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a previously-encrypted recipient identifier. Returns null on
    /// decryption failure (key-rotation drift) so callers can degrade gracefully
    /// to a "PII unavailable" verify result instead of throwing.
    /// </summary>
    string? Decrypt(byte[] cipher);

    /// <summary>
    /// Keyed-HMAC of the plaintext, used to populate <c>RecipientUserIdHash</c> /
    /// <c>RecipientEmailHash</c>. Same plaintext + same key always yields the same
    /// hash, so analytics queries can locate rows by user without decryption.
    /// </summary>
    byte[] Hash(string plaintext);
}
