using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public class UserCleanupService(
    ICollectionAclRepository aclRepo,
    IShareRepository shareRepo,
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

        var userShares = (await shareRepo.GetByUserAsync(userId, take: int.MaxValue, cancellationToken: ct))
            .Where(s => !s.RevokedAt.HasValue)
            .ToList();
        foreach (var share in userShares)
        {
            share.RevokedAt = DateTime.UtcNow;
            await shareRepo.UpdateAsync(share, ct);
        }

        logger.LogInformation("Cleaned up user {UserId}: removed {AclCount} ACLs, revoked {ShareCount} shares",
            userId, userAcls.Count, userShares.Count);

        await audit.LogAsync("user.cleanup", Constants.ScopeTypes.User, null, userId,
            new() { ["aclsRemoved"] = userAcls.Count, ["sharesRevoked"] = userShares.Count }, ct);

        return (userAcls.Count, userShares.Count);
    }
}
