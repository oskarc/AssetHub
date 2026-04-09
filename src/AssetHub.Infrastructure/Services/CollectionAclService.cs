using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Caching.Hybrid;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Groups repository dependencies for <see cref="CollectionAclService"/>
/// to keep the constructor parameter count manageable.
/// </summary>
public sealed record CollectionAclRepositories(
    ICollectionRepository CollectionRepo,
    ICollectionAclRepository AclRepo);

/// <summary>
/// Unified ACL management for collections. Replaces the duplicated logic
/// that existed in both CollectionEndpoints and AdminEndpoints.
/// </summary>
public sealed class CollectionAclService(
    CollectionAclRepositories repos,
    ICollectionAuthorizationService authService,
    IUserLookupService userLookup,
    IKeycloakUserService keycloakUserService,
    IAuditService audit,
    HybridCache cache,
    CurrentUser currentUser) : ICollectionAclService, IAdminCollectionAclService
{
    private readonly ICollectionRepository _collectionRepo = repos.CollectionRepo;
    private readonly ICollectionAclRepository _aclRepo = repos.AclRepo;

    // ── Manager self-service (used by CollectionEndpoints) ───────────────────

    public async Task<ServiceResult<IEnumerable<CollectionAclResponseDto>>> GetAclsAsync(
        Guid collectionId, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var canManage = await authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return ServiceError.Forbidden();

        var acls = await _aclRepo.GetByCollectionAsync(collectionId, ct);
        var userIds = acls.Where(a => a.PrincipalType == PrincipalType.User).Select(a => a.PrincipalId);

        // These are independent Keycloak DB/API calls — run in parallel
        var nameMapTask = userLookup.GetUserNamesAsync(userIds, ct);
        var emailMapTask = userLookup.GetUserEmailsAsync(userIds, ct);
        var adminIdsTask = keycloakUserService.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Admin, ct);
        await Task.WhenAll(nameMapTask, emailMapTask, adminIdsTask);
        var nameMap = nameMapTask.Result;
        var emailMap = emailMapTask.Result;
        var adminIds = adminIdsTask.Result;

        var dtos = acls.Select(a => new CollectionAclResponseDto
        {
            Id = a.Id,
            PrincipalType = a.PrincipalType.ToDbString(),
            PrincipalId = a.PrincipalId,
            PrincipalName = ResolvePrincipalName(a, nameMap),
            PrincipalEmail = a.PrincipalType == PrincipalType.User && emailMap.TryGetValue(a.PrincipalId, out var email) ? email : null,
            Role = a.Role.ToDbString(),
            CreatedAt = a.CreatedAt,
            IsSystemAdmin = a.PrincipalType == PrincipalType.User && adminIds.Contains(a.PrincipalId)
        });

        return new ServiceResult<IEnumerable<CollectionAclResponseDto>> { Value = dtos };
    }

    public async Task<ServiceResult<CollectionAclResponseDto>> SetAccessAsync(
        Guid collectionId, string principalType, string principalId, string role,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var canManage = await authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return ServiceError.Forbidden();

        if (string.IsNullOrWhiteSpace(principalType) || string.IsNullOrWhiteSpace(principalId) || string.IsNullOrWhiteSpace(role))
            return ServiceError.BadRequest("PrincipalType, PrincipalId, and Role are required");

        if (!RoleHierarchy.AllRoles.Contains(role))
            return ServiceError.BadRequest("Invalid role");

        // Role escalation guard
        var callerRole = await authService.GetUserRoleAsync(userId, collectionId, ct);
        if (!RoleHierarchy.CanGrantRole(callerRole, role))
            return ServiceError.BadRequest($"You cannot grant the '{role}' role because it exceeds your own access level");

        var acl = await _aclRepo.SetAccessAsync(collectionId, principalType, principalId, role, ct);

        await audit.LogAsync("acl.set", Constants.ScopeTypes.Collection, collectionId, userId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId, ["role"] = role },
            ct);

        if (principalType == Constants.PrincipalTypes.User)
            await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAccessTag(principalId), ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAcl, ct);

        string? principalName = null;
        string? principalEmail = null;
        if (principalType == Constants.PrincipalTypes.User)
        {
            var nameTask = userLookup.GetUserNameAsync(principalId, ct);
            var emailMapTask = userLookup.GetUserEmailsAsync(new[] { principalId }, ct);
            await Task.WhenAll(nameTask, emailMapTask);
            principalName = nameTask.Result;
            emailMapTask.Result.TryGetValue(principalId, out principalEmail);
        }

        return new CollectionAclResponseDto
        {
            Id = acl.Id,
            PrincipalType = acl.PrincipalType.ToDbString(),
            PrincipalId = acl.PrincipalId,
            PrincipalName = principalName,
            PrincipalEmail = principalEmail,
            Role = acl.Role.ToDbString(),
            CreatedAt = acl.CreatedAt
        };
    }

    public async Task<ServiceResult> RevokeAccessAsync(
        Guid collectionId, string principalType, string principalId, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var canManage = await authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return ServiceError.Forbidden();

        // Role escalation guard
        var callerRole = await authService.GetUserRoleAsync(userId, collectionId, ct);
        var targetAcl = await _aclRepo.GetByPrincipalAsync(collectionId, principalType, principalId, ct);
        if (targetAcl is not null && !RoleHierarchy.CanRevokeRole(callerRole, targetAcl.Role.ToDbString()))
            return ServiceError.BadRequest($"You cannot revoke a '{targetAcl.Role.ToDbString()}' role because it exceeds your own access level");

        await _aclRepo.RevokeAccessAsync(collectionId, principalType, principalId, ct);

        await audit.LogAsync("acl.revoked", Constants.ScopeTypes.Collection, collectionId, userId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId },
            ct);

        if (principalType == Constants.PrincipalTypes.User)
            await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAccessTag(principalId), ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAcl, ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<List<UserSearchResultDto>>> SearchUsersForAclAsync(
        Guid collectionId, string? query, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var canManage = await authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return ServiceError.Forbidden();

        List<(string Id, string Username, string? Email)> matchedUsers;

        if (!string.IsNullOrWhiteSpace(query))
        {
            matchedUsers = await userLookup.SearchUsersAsync(query.Trim(), 50, ct);
        }
        else
        {
            var allUsers = await userLookup.GetAllUsersAsync(ct);
            matchedUsers = allUsers.Select(u => (u.Id, u.Username, u.Email)).ToList();
        }

        var existingAcls = await _aclRepo.GetByCollectionAsync(collectionId, ct);
        var existingUserIds = existingAcls
            .Where(a => a.PrincipalType == PrincipalType.User)
            .Select(a => a.PrincipalId)
            .ToHashSet();

        var result = matchedUsers
            .Where(u => !existingUserIds.Contains(u.Id))
            .Select(u => new UserSearchResultDto { Id = u.Id, Username = u.Username, Email = u.Email })
            .OrderBy(u => u.Username)
            .Take(50)
            .ToList();

        return result;
    }

    // ── Admin-only operations ────────────────────────────────────────────────

    public async Task<ServiceResult<AccessUpdatedResponse>> AdminSetAccessAsync(
        Guid collectionId, SetCollectionAccessRequest request, CancellationToken ct)
    {
        if (!await _collectionRepo.ExistsAsync(collectionId, ct))
            return ServiceError.NotFound("Collection not found");

        if (string.IsNullOrWhiteSpace(request.PrincipalId))
            return ServiceError.BadRequest("PrincipalId is required");

        var principalType = request.PrincipalType ?? Constants.PrincipalTypes.User;
        var principalId = request.PrincipalId;

        if (principalType == Constants.PrincipalTypes.User)
        {
            if (!Guid.TryParse(request.PrincipalId, out _))
            {
                var resolvedUserId = await userLookup.GetUserIdByUsernameAsync(request.PrincipalId, ct);
                if (resolvedUserId is null)
                    return ServiceError.BadRequest($"User '{request.PrincipalId}' not found");
                principalId = resolvedUserId;
            }
            else
            {
                var username = await userLookup.GetUserNameAsync(request.PrincipalId, ct);
                if (username is null)
                    return ServiceError.BadRequest($"User with ID '{request.PrincipalId}' not found");
            }
        }

        var targetRole = request.Role!.ToLowerInvariant();
        if (!RoleHierarchy.AllRoles.Contains(targetRole))
            return ServiceError.BadRequest($"Invalid role '{targetRole}'");

        var acl = await _aclRepo.SetAccessAsync(collectionId, principalType, principalId, targetRole, ct);

        var adminUserId = currentUser.UserId;
        await audit.LogAsync("acl.set", Constants.ScopeTypes.Collection, collectionId, adminUserId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId, ["role"] = targetRole, ["admin"] = true },
            ct);

        if (principalType == Constants.PrincipalTypes.User)
            await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAccessTag(principalId), ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAcl, ct);

        return new AccessUpdatedResponse
        {
            Message = "Access updated",
            CollectionId = collectionId,
            PrincipalId = principalId,
            Role = acl.Role.ToDbString()
        };
    }

    public async Task<ServiceResult<AccessRevokedResponse>> AdminRevokeAccessAsync(
        Guid collectionId, string principalType, string principalId, CancellationToken ct)
    {
        if (!await _collectionRepo.ExistsAsync(collectionId, ct))
            return ServiceError.NotFound("Collection not found");

        await _aclRepo.RevokeAccessAsync(collectionId, principalType, principalId, ct);

        var adminUserId = currentUser.UserId;
        await audit.LogAsync("acl.revoked", Constants.ScopeTypes.Collection, collectionId, adminUserId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId, ["admin"] = true },
            ct);

        if (principalType == Constants.PrincipalTypes.User)
            await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAccessTag(principalId), ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAcl, ct);

        return new AccessRevokedResponse
        {
            Message = "Access revoked",
            CollectionId = collectionId,
            PrincipalId = principalId
        };
    }

    public async Task<ServiceResult<List<CollectionAccessDto>>> GetCollectionAccessTreeAsync(CancellationToken ct)
    {
        var collections = await _collectionRepo.GetAllWithAclsAsync(ct);
        var allCollections = collections.ToList();

        var allUserIds = allCollections
            .SelectMany(c => c.Acls.Where(a => a.PrincipalType == PrincipalType.User).Select(a => a.PrincipalId))
            .Distinct()
            .ToList();
        var userNames = await userLookup.GetUserNamesAsync(allUserIds, ct);
        var userEmails = await userLookup.GetUserEmailsAsync(allUserIds, ct);
        var adminIds = await keycloakUserService.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Admin, ct);

        var result = allCollections
            .Select(c => CollectionTreeHelper.ToAccessDto(c, userNames, userEmails, adminIds))
            .ToList();

        return result;
    }

    private static string? ResolvePrincipalName(CollectionAcl acl, Dictionary<string, string> nameMap)
    {
        if (acl.PrincipalType != PrincipalType.User)
            return null;

        return nameMap.TryGetValue(acl.PrincipalId, out var name)
            ? name
            : $"Deleted User ({acl.PrincipalId[..Math.Min(8, acl.PrincipalId.Length)]})";
    }
}
