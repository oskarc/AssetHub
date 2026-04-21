namespace AssetHub.Application.Services;

/// <summary>
/// Protects and unprotects connector secrets (e.g. the S3 <c>secretKey</c>) stored
/// inside <c>Migration.SourceConfig</c>. Backed by ASP.NET Core Data Protection so
/// payloads are bound to the application's key ring and can't be read outside the
/// running application.
/// </summary>
public interface IMigrationSecretProtector
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns a base64-encoded ciphertext
    /// safe to persist inside a JSONB dictionary.
    /// </summary>
    string Protect(string plaintext);

    /// <summary>
    /// Reverses <see cref="Protect"/>. Throws <see cref="System.Security.Cryptography.CryptographicException"/>
    /// if the payload was produced by a different protector or key ring.
    /// </summary>
    string Unprotect(string protectedPayload);
}
