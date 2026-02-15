using Dam.Application.Repositories;
using Dam.Application.Services;
using Microsoft.Extensions.Logging;

namespace Dam.Infrastructure.Services;

/// <summary>
/// Synchronizes application data with Keycloak by detecting and cleaning up
/// references to users that have been deleted from Keycloak.
/// </summary>
public class UserSyncService(
    ICollectionAclRepository aclRepo,
    IShareRepository shareRepo,
    IUserLookupService userLookup,
    IUserCleanupService cleanupService,
    IAuditService audit,
    ILogger<UserSyncService> logger) : IUserSyncService
{
    public async Task<UserSyncResult> SyncDeletedUsersAsync(bool dryRun = false, CancellationToken ct = default)
    {
        logger.LogInformation("Starting user sync (dryRun={DryRun})", dryRun);

        // 1. Collect all distinct user IDs referenced in ACLs and shares
        var allAcls = (await aclRepo.GetAllAsync(ct))
            .Where(a => a.PrincipalType == "user")
            .ToList();

        var allShares = await shareRepo.GetAllAsync(cancellationToken: ct);

        var referencedUserIds = allAcls.Select(a => a.PrincipalId)
            .Concat(allShares.Select(s => s.CreatedByUserId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (referencedUserIds.Count == 0)
        {
            logger.LogInformation("No user references found in app data");
            return new UserSyncResult { DryRun = dryRun };
        }

        // 2. Check which users still exist in Keycloak
        var existingIds = await userLookup.GetExistingUserIdsAsync(referencedUserIds, ct);
        var deletedIds = referencedUserIds
            .Where(id => !existingIds.Contains(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new UserSyncResult
        {
            TotalReferencedUsers = referencedUserIds.Count,
            ActiveUsers = existingIds.Count,
            DeletedUsers = deletedIds.Count,
            DryRun = dryRun
        };

        if (deletedIds.Count == 0)
        {
            logger.LogInformation("All {Count} referenced users still exist in Keycloak", referencedUserIds.Count);
            return result;
        }

        logger.LogWarning("Found {Count} deleted users with orphaned app data", deletedIds.Count);

        // 3. Process each deleted user
        foreach (var userId in deletedIds)
        {
            var userAcls = allAcls.Where(a => a.PrincipalId == userId).ToList();
            var userShares = allShares
                .Where(s => s.CreatedByUserId == userId && !s.RevokedAt.HasValue)
                .ToList();

            var info = new DeletedUserInfo
            {
                UserId = userId,
                AclCount = userAcls.Count,
                ShareCount = userShares.Count
            };
            result.DeletedUserDetails.Add(info);

            if (dryRun)
            {
                logger.LogInformation("[DRY RUN] Deleted user {UserId}: would remove {AclCount} ACLs, revoke {ShareCount} shares",
                    userId, userAcls.Count, userShares.Count);
                result.AclsRemoved += userAcls.Count;
                result.SharesRevoked += userShares.Count;
                continue;
            }

            var (aclsRemoved, sharesRevoked) = await cleanupService.CleanupUserDataAsync(userId, ct);
            result.AclsRemoved += aclsRemoved;
            result.SharesRevoked += sharesRevoked;

            await audit.LogAsync("user.sync.cleanup", "user",
                Guid.TryParse(userId, out var uid) ? uid : null, null,
                new Dictionary<string, object>
                {
                    ["deletedUserId"] = userId,
                    ["aclsRemoved"] = userAcls.Count.ToString(),
                    ["sharesRevoked"] = userShares.Count.ToString()
                }, httpContext: null, ct);
        }

        logger.LogInformation("User sync completed: {Deleted} deleted users, {Acls} ACLs removed, {Shares} shares revoked",
            result.DeletedUsers, result.AclsRemoved, result.SharesRevoked);

        return result;
    }
}
