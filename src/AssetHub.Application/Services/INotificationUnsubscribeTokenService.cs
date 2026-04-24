namespace AssetHub.Application.Services;

/// <summary>
/// One-way signed tokens that encode the unsubscribe intent into the email
/// URL itself — no database lookup needed. The payload is
/// <c>{userId, category, stamp}</c>; <c>stamp</c> is the user's current
/// <c>NotificationPreferences.UnsubscribeTokenHash</c>, which lets a user
/// invalidate every outstanding unsubscribe link by rotating the hash.
///
/// Uses ASP.NET Core Data Protection under the purpose string
/// <c>Constants.DataProtection.NotificationUnsubscribeProtector</c>; tampering
/// with the URL fails at <c>Unprotect</c>.
/// </summary>
public interface INotificationUnsubscribeTokenService
{
    /// <summary>
    /// Builds a URL-safe token for the given recipient + category + stamp.
    /// Caller composes the final URL
    /// (<c>{BaseUrl}/api/v1/notifications/unsubscribe?token={token}</c>).
    /// </summary>
    string CreateToken(string userId, string category, string stamp);

    /// <summary>
    /// Unprotects a token and returns the embedded payload, or null when the
    /// token is malformed, tampered, or otherwise invalid. Stamp validation
    /// against the current preferences row is the caller's job.
    /// </summary>
    UnsubscribeTokenPayload? TryParseToken(string token);
}

public sealed record UnsubscribeTokenPayload(string UserId, string Category, string Stamp);
