using System.Security.Cryptography;
using System.Text;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services.Watermarking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services.Watermarking;

/// <summary>
/// Encrypts / decrypts watermark recipient PII at rest and computes HMAC hashes for
/// the indexable hash columns (T5-WMK-01). Wraps <c>IDataProtector</c> with purpose
/// <see cref="Constants.DataProtection.WatermarkRecipientProtector"/> for the
/// encrypt path and uses an HKDF-stretched HMAC-SHA256 keyed by a deployment-scoped
/// base64 secret for the hash path.
///
/// Decryption errors return null (key-rotation drift) rather than throwing — the
/// verify endpoint surfaces "PII unavailable" to the admin instead of crashing.
/// </summary>
public sealed class RecipientCryptoService(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<WatermarkSettings> settings,
    ILogger<RecipientCryptoService> logger) : IRecipientCryptoService
{
    private readonly IDataProtector _protector = dataProtectionProvider
        .CreateProtector(Constants.DataProtection.WatermarkRecipientProtector);

    private readonly byte[] _hmacKey = ParseHmacKey(settings.Value.HmacKeyBase64);

    /// <inheritdoc />
    public byte[] Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
    }

    /// <inheritdoc />
    public string? Decrypt(byte[] cipher)
    {
        if (cipher is null || cipher.Length == 0) return null;

        try
        {
            return Encoding.UTF8.GetString(_protector.Unprotect(cipher));
        }
        catch (CryptographicException ex)
        {
            // Key-rotation drift or corrupted ciphertext. The verify endpoint
            // surfaces "PII unavailable" rather than failing the whole verify.
            logger.LogWarning(ex,
                "Watermark recipient PII decryption failed (length: {Length} bytes)",
                cipher.Length);
            return null;
        }
    }

    /// <inheritdoc />
    public byte[] Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(plaintext));
    }

    private static byte[] ParseHmacKey(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidOperationException(
                "WatermarkSettings.HmacKeyBase64 is not configured. " +
                "Generate with `openssl rand -base64 32` and set via env var Watermarking__HmacKeyBase64.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "WatermarkSettings.HmacKeyBase64 is not a valid base64 string.", ex);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException(
                $"WatermarkSettings.HmacKeyBase64 must decode to exactly 32 bytes (got {key.Length}).");
        }

        return key;
    }
}
