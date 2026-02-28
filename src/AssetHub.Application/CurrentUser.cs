namespace AssetHub.Application;

/// <summary>
/// Represents the identity and role of the currently authenticated user.
/// Registered as a scoped service — resolved from HttpContext on each request.
/// Services inject this instead of manually extracting claims from HttpContext.
/// </summary>
public class CurrentUser
{
    /// <summary>
    /// Anonymous/empty user used when HttpContext is unavailable (e.g., background jobs).
    /// Services should check <see cref="IsAuthenticated"/> before relying on UserId.
    /// </summary>
    public static CurrentUser Anonymous { get; } = new("", false);

    /// <summary>User ID from the "sub" claim.</summary>
    public string UserId { get; }

    /// <summary>True when the user has the global "admin" realm role.</summary>
    public bool IsSystemAdmin { get; }

    /// <summary>True when a valid user ID is present.</summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);

    public CurrentUser(string userId, bool isSystemAdmin)
    {
        UserId = userId;
        IsSystemAdmin = isSystemAdmin;
    }
}
