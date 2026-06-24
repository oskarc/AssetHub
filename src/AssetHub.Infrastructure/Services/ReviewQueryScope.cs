using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Shared scope + name resolution for the Reviews query collaborators
/// (<see cref="ReviewQueueQuery"/> / <see cref="ReviewDecisionsQuery"/>). Static, with the
/// dependencies passed in, so neither collaborator duplicates the logic nor owns the other's
/// dependencies — both read models scope identically.
/// </summary>
internal static class ReviewQueryScope
{
    /// <summary>
    /// Resolves the caller's review scope. System admins see everything (the second
    /// tuple element is unused). Everyone else is scoped to the collections where they
    /// hold manager+, computed over only their accessible collections to keep it cheap.
    /// </summary>
    public static async Task<(bool IsAdmin, List<Guid> ManagerCollectionIds)> ResolveManagerScopeAsync(
        ICollectionRepository collectionRepo,
        ICollectionAuthorizationService authService,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (currentUser.IsSystemAdmin)
            return (true, []);

        var accessible = await collectionRepo.GetAccessibleCollectionsAsync(currentUser.UserId, ct);
        var accessibleIds = accessible.Select(c => c.Id).ToList();
        if (accessibleIds.Count == 0)
            return (false, []);

        var managerIds = await authService.FilterAccessibleAsync(
            currentUser.UserId, accessibleIds, RoleHierarchy.Roles.Manager, ct);
        return (false, managerIds);
    }

    /// <summary>Resolves Keycloak subs to display names; unresolved ids are simply absent so callers fall back to the raw id.</summary>
    public static async Task<Dictionary<string, string>> ResolveNamesAsync(
        IUserLookupService userLookup,
        IEnumerable<string> userIds,
        CancellationToken ct)
    {
        var distinct = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        return distinct.Count == 0
            ? new Dictionary<string, string>()
            : await userLookup.GetUserNamesAsync(distinct, ct);
    }
}
