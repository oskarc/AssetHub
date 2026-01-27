using System.Security.Claims;

namespace AssetHub.Endpoints;

/// <summary>
/// Extension methods for ClaimsPrincipal to provide consistent claim extraction.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the user ID from the claims principal.
    /// Checks both "sub" (standard OIDC) and ClaimTypes.NameIdentifier claims.
    /// </summary>
    /// <param name="user">The claims principal representing the current user.</param>
    /// <returns>The user ID, or null if not found.</returns>
    public static string? GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// Gets the user ID from the claims principal, or a fallback value if not found.
    /// </summary>
    /// <param name="user">The claims principal representing the current user.</param>
    /// <param name="fallback">The fallback value to return if no user ID is found.</param>
    /// <returns>The user ID, or the fallback value.</returns>
    public static string GetUserIdOrDefault(this ClaimsPrincipal user, string fallback = "unknown")
    {
        return user.GetUserId() ?? fallback;
    }
}
