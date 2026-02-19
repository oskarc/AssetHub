using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Dam.Infrastructure.Services;

/// <inheritdoc />
public class UserCleanupService(
    ICollectionAclRepository aclRepo,
    IShareRepository shareRepo,
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

        return (userAcls.Count, userShares.Count);
    }
}
