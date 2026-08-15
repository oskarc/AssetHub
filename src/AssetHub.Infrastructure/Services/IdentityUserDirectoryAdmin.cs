using AssetHub.Application.Services;
using AssetHub.Application.Services.Email;
using AssetHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// <see cref="IUserDirectoryAdmin"/> backed by the local ASP.NET Core Identity
/// stores. Selected when <c>Auth:Provider</c> is <c>Identity</c>.
/// </summary>
/// <remarks>
/// The Keycloak implementation talks to a remote admin API and therefore wraps
/// every call in HTTP error handling. Here the stores are local, so failures
/// arrive as <see cref="IdentityResult"/> errors instead; they are translated
/// into <see cref="InvalidOperationException"/> to match the exception-based
/// contract the interface already had.
/// </remarks>
public sealed class IdentityUserDirectoryAdmin(
    UserManager<AppUser> userManager,
    IPasswordResetLinkSender<AppUser> resetLinkSender,
    ILogger<IdentityUserDirectoryAdmin> logger) : IUserDirectoryAdmin
{
    public async Task<string> CreateUserAsync(
        string username,
        string email,
        string firstName,
        string lastName,
        string password,
        bool temporaryPassword = true,
        CancellationToken ct = default)
    {
        var displayName = string.Join(' ', new[] { firstName, lastName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

        var user = new AppUser
        {
            UserName = username,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to create user '{username}': {Describe(result)}");

        logger.LogInformation("Created Identity user {UserName} ({UserId})", username, user.Id);
        return user.Id;
    }

    public async Task SendExecuteActionsEmailAsync(
        string userId,
        IEnumerable<string> actions,
        int? lifespan = null,
        CancellationToken ct = default)
    {
        // Only UPDATE_PASSWORD is ever requested by callers. Anything else would
        // silently do nothing under Identity, so it fails loudly instead.
        var requested = actions?.ToList() ?? [];
        var unsupported = requested.Where(a => !string.Equals(a, "UPDATE_PASSWORD", StringComparison.Ordinal)).ToList();
        if (unsupported.Count > 0)
            throw new NotSupportedException(
                $"The Identity provider supports only the UPDATE_PASSWORD action; got: {string.Join(", ", unsupported)}");

        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found");

        await resetLinkSender.SendAsync(user, isNewAccount: false, ct);
    }

    public async Task DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            // Idempotent, matching the Keycloak implementation's behaviour for an
            // already-absent user.
            logger.LogInformation("Delete requested for unknown user {UserId} — nothing to do", userId);
            return;
        }

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to delete user '{userId}': {Describe(result)}");

        logger.LogInformation("Deleted Identity user {UserId}", userId);
    }

    public async Task<HashSet<string>> GetRealmRoleMemberIdsAsync(string roleName, CancellationToken ct = default)
    {
        var members = await userManager.GetUsersInRoleAsync(roleName);
        return members.Select(u => u.Id).ToHashSet();
    }

    public async Task AssignRealmRoleAsync(string userId, string roleName, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found");

        if (await userManager.IsInRoleAsync(user, roleName)) return;

        var result = await userManager.AddToRoleAsync(user, roleName);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to grant '{roleName}' to '{userId}': {Describe(result)}");

        logger.LogInformation("Granted role {Role} to {UserId}", roleName, userId);
    }

    public async Task RemoveRealmRoleAsync(string userId, string roleName, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found");

        if (!await userManager.IsInRoleAsync(user, roleName)) return;

        var result = await userManager.RemoveFromRoleAsync(user, roleName);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to remove '{roleName}' from '{userId}': {Describe(result)}");

        logger.LogInformation("Removed role {Role} from {UserId}", roleName, userId);
    }

    private static string Describe(IdentityResult result)
        => string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
}
