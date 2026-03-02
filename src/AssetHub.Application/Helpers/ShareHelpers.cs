using System.Security.Cryptography;
using System.Text;

namespace AssetHub.Application.Helpers;

/// <summary>
/// Shared helpers for share-link token handling, validation, and status determination.
/// </summary>
public static class ShareHelpers
{
    /// <summary>
    /// Computes the SHA-256 hash of a share token for secure storage/lookup.
    /// </summary>
    public static string ComputeTokenHash(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Generates a cryptographically secure URL-safe token string.
    /// </summary>
    public static string GenerateToken(int byteLength = 32)
    {
        var tokenBytes = new byte[byteLength];
        RandomNumberGenerator.Fill(tokenBytes);
        return Convert.ToBase64String(tokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Determines the display status of a share based on its state.
    /// </summary>
    public static string GetShareStatus(DateTime? revokedAt, DateTime? expiresAt)
    {
        if (revokedAt.HasValue)
            return Constants.ShareStatus.Revoked;
        if (expiresAt < DateTime.UtcNow)
            return Constants.ShareStatus.Expired;
        return Constants.ShareStatus.Active;
    }

    /// <summary>
    /// Validates a share's accessibility. Returns an error code or null if valid.
    /// Error codes: Constants.ShareErrorCodes.Revoked, Constants.ShareErrorCodes.Expired
    /// Does NOT check password — that must be done separately.
    /// </summary>
    public static string? ValidateShareAccess(DateTime? revokedAt, DateTime? expiresAt)
    {
        if (revokedAt.HasValue)
            return Constants.ShareErrorCodes.Revoked;
        if (expiresAt < DateTime.UtcNow)
            return Constants.ShareErrorCodes.Expired;
        return null;
    }
}
