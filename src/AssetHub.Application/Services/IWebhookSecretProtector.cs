namespace AssetHub.Application.Services;

/// <summary>
/// Wraps ASP.NET Core Data Protection for webhook signing secrets. The
/// dispatcher unprotects on every send so plaintext lives only in
/// transient memory; persisted state is only the encrypted payload.
/// </summary>
public interface IWebhookSecretProtector
{
    /// <summary>Generates a new random plaintext secret (URL-safe base64, 32 bytes of entropy).</summary>
    string GeneratePlaintext();

    string Protect(string plaintext);

    /// <summary>
    /// Reverses <see cref="Protect"/>. Throws
    /// <see cref="System.Security.Cryptography.CryptographicException"/>
    /// when the payload was produced by a different protector or key ring.
    /// </summary>
    string Unprotect(string protectedPayload);
}
