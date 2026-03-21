using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public class UserCleanupService(
    ICollectionAclRepository aclRepo,
    IAuditService audit,
    ILogger<UserCleanupService> logger) : IUserCleanupService
{
    public async Task<(int AclsRemoved, int SharesRevoked)> CleanupUserDataAsync(
        string userId, CancellationToken ct = default)
    {
        var userAcls = (await aclRepo.GetByUserAsync(userId, ct)).ToList();
        foreach (var acl in userAcls)
        {
            await aclRepo.RevokeAccessAsync(acl.CollectionId, acl.PrincipalType.ToDbString(), acl.PrincipalId, ct);
        }

        logger.LogInformation("Cleaned up user {UserId}: removed {AclCount} ACLs, shares preserved",
            userId, userAcls.Count);

        await audit.LogAsync("user.cleanup", Constants.ScopeTypes.User, null, userId,
            new() { ["aclsRemoved"] = userAcls.Count }, ct);

        return (userAcls.Count, 0);
    }
}
