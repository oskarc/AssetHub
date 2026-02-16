using System.Security.Claims;

namespace Dam.Application;

/// <summary>
/// Extension methods for ClaimsPrincipal to provide consistent claim extraction.
/// Lives in Dam.Application so it can be used across all layers (endpoints, services, UI).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Gets the user ID from the claims principal.
    /// Checks both "sub" (standard OIDC) and ClaimTypes.NameIdentifier claims.
    /// </summary>
    public static string? GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// Gets the user ID from the claims principal, or throws if not found.
    /// Use this in endpoints that require authentication — if the ID is missing,
    /// the auth middleware has failed and we must not silently continue.
    /// </summary>
    public static string GetRequiredUserId(this ClaimsPrincipal user)
    {
        return user.GetUserId()
            ?? throw new UnauthorizedAccessException("User ID claim (sub) is missing from the authenticated principal");
    }

    /// <summary>
    /// Gets the user ID from the claims principal, or a fallback value if not found.
    /// Prefer <see cref="GetRequiredUserId"/> in authenticated endpoints.
    /// </summary>
    [Obsolete("Use GetRequiredUserId() in authenticated endpoints. This overload masks missing claims.")]
    public static string GetUserIdOrDefault(this ClaimsPrincipal user, string fallback = "unknown")
    {
        return user.GetUserId() ?? fallback;
    }

    /// <summary>
    /// Checks if the user has the global admin role (Keycloak realm role).
    /// </summary>
    public static bool IsGlobalAdmin(this ClaimsPrincipal user)
    {
        return user.IsInRole(RoleHierarchy.Roles.Admin);
    }

    /// <summary>
    /// Gets the user's display name from claims.
    /// </summary>
    public static string? GetDisplayName(this ClaimsPrincipal user)
    {
        return user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value;
    }
}
