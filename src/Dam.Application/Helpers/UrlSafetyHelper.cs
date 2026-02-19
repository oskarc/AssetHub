namespace Dam.Application.Helpers;

/// <summary>
/// Validates return URLs to prevent open redirect attacks.
/// Only allows navigation to known internal routes.
/// </summary>
public static class UrlSafetyHelper
{
    /// <summary>
    /// Known internal route prefixes that are safe to redirect to.
    /// </summary>
    private static readonly string[] AllowedPrefixes =
    [
        "/collections",
        "/assets",
        "/all-assets",
        "/admin",
        "/share",
        "/login",
    ];

    /// <summary>
    /// Validates that a return URL is safe for navigation.
    /// Returns the URL if valid, or the fallback if not.
    /// </summary>
    /// <param name="returnUrl">The URL to validate.</param>
    /// <param name="fallback">Fallback URL if validation fails. Defaults to "/".</param>
    /// <returns>The validated URL or the fallback.</returns>
    public static string SafeReturnUrl(string? returnUrl, string fallback = "/")
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return fallback;

        // Must be a relative URI
        if (!Uri.TryCreate(returnUrl, UriKind.Relative, out _))
            return fallback;

        // Block protocol-relative URLs (//evil.com)
        if (returnUrl.StartsWith("//"))
            return fallback;

        // Must start with a known internal route prefix
        foreach (var prefix in AllowedPrefixes)
        {
            if (returnUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return returnUrl;
        }

        // Allow bare root
        if (returnUrl == "/")
            return returnUrl;

        return fallback;
    }
}
