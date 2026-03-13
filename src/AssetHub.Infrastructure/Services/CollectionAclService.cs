using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Unified ACL management for collections. Replaces the duplicated logic
/// that existed in both CollectionEndpoints and AdminEndpoints.
/// </summary>
public class CollectionAclService : ICollectionAclService, IAdminCollectionAclService
{
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICollectionAclRepository _aclRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IUserLookupService _userLookup;
    private readonly IKeycloakUserService _keycloakUserService;
    private readonly IAuditService _audit;
    private readonly CurrentUser _currentUser;

    public CollectionAclService(
        ICollectionRepository collectionRepo,
        ICollectionAclRepository aclRepo,
        ICollectionAuthorizationService authService,
        IUserLookupService userLookup,
        IKeycloakUserService keycloakUserService,
        IAuditService audit,
        CurrentUser currentUser)
    {
        _collectionRepo = collectionRepo;
        _aclRepo = aclRepo;
        _authService = authService;
        _userLookup = userLookup;
        _keycloakUserService = keycloakUserService;
        _audit = audit;
        _currentUser = currentUser;
    }

    // ── Manager self-service (used by CollectionEndpoints) ───────────────────

    public async Task<ServiceResult<IEnumerable<CollectionAclResponseDto>>> GetAclsAsync(
        Guid collectionId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var canManage = await _authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return ServiceError.Forbidden();

        var acls = await _aclRepo.GetByCollectionAsync(collectionId, ct);
        var userIds = acls.Where(a => a.PrincipalType == PrincipalType.User).Select(a => a.PrincipalId);
        var nameMap = await _userLookup.GetUserNamesAsync(userIds, ct);
        var emailMap = await _userLookup.GetUserEmailsAsync(userIds, ct);
        var adminIds = await _keycloakUserService.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Admin, ct);

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
        var userId = _currentUser.UserId;

        var canManage = await _authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return ServiceError.Forbidden();

        if (string.IsNullOrWhiteSpace(principalType) || string.IsNullOrWhiteSpace(principalId) || string.IsNullOrWhiteSpace(role))
            return ServiceError.BadRequest("PrincipalType, PrincipalId, and Role are required");

        if (!RoleHierarchy.AllRoles.Contains(role))
            return ServiceError.BadRequest("Invalid role");

        // Role escalation guard
        var callerRole = await _authService.GetUserRoleAsync(userId, collectionId, ct);
        if (!RoleHierarchy.CanGrantRole(callerRole, role))
            return ServiceError.BadRequest($"You cannot grant the '{role}' role because it exceeds your own access level");

        var acl = await _aclRepo.SetAccessAsync(collectionId, principalType, principalId, role, ct);

        await _audit.LogAsync("acl.set", Constants.ScopeTypes.Collection, collectionId, userId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId, ["role"] = role },
            ct);

        string? principalName = null;
        string? principalEmail = null;
        if (principalType == Constants.PrincipalTypes.User)
        {
            principalName = await _userLookup.GetUserNameAsync(principalId, ct);
            var emailMap = await _userLookup.GetUserEmailsAsync(new[] { principalId }, ct);
            emailMap.TryGetValue(principalId, out principalEmail);
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
        var userId = _currentUser.UserId;

        var canManage = await _authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return ServiceError.Forbidden();

        // Role escalation guard
        var callerRole = await _authService.GetUserRoleAsync(userId, collectionId, ct);
        var targetAcl = await _aclRepo.GetByPrincipalAsync(collectionId, principalType, principalId, ct);
        if (targetAcl != null && !RoleHierarchy.CanRevokeRole(callerRole, targetAcl.Role.ToDbString()))
            return ServiceError.BadRequest($"You cannot revoke a '{targetAcl.Role.ToDbString()}' role because it exceeds your own access level");

        await _aclRepo.RevokeAccessAsync(collectionId, principalType, principalId, ct);

        await _audit.LogAsync("acl.revoked", Constants.ScopeTypes.Collection, collectionId, userId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId },
            ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<List<UserSearchResultDto>>> SearchUsersForAclAsync(
        Guid collectionId, string? query, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var canManage = await _authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return ServiceError.Forbidden();

        var allUsers = await _userLookup.GetAllUsersAsync(ct);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            allUsers = allUsers
                .Where(u => u.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (u.Email?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var existingAcls = await _aclRepo.GetByCollectionAsync(collectionId, ct);
        var existingUserIds = existingAcls
            .Where(a => a.PrincipalType == PrincipalType.User)
            .Select(a => a.PrincipalId)
            .ToHashSet();

        var result = allUsers
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
                var resolvedUserId = await _userLookup.GetUserIdByUsernameAsync(request.PrincipalId, ct);
                if (resolvedUserId == null)
                    return ServiceError.BadRequest($"User '{request.PrincipalId}' not found");
                principalId = resolvedUserId;
            }
            else
            {
                var username = await _userLookup.GetUserNameAsync(request.PrincipalId, ct);
                if (username == null)
                    return ServiceError.BadRequest($"User with ID '{request.PrincipalId}' not found");
            }
        }

        var targetRole = request.Role!.ToLowerInvariant();
        if (!RoleHierarchy.AllRoles.Contains(targetRole))
            return ServiceError.BadRequest($"Invalid role '{targetRole}'");

        var acl = await _aclRepo.SetAccessAsync(collectionId, principalType, principalId, targetRole, ct);

        var adminUserId = _currentUser.UserId;
        await _audit.LogAsync("acl.set", Constants.ScopeTypes.Collection, collectionId, adminUserId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId, ["role"] = targetRole, ["admin"] = true },
            ct);

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

        var adminUserId = _currentUser.UserId;
        await _audit.LogAsync("acl.revoked", Constants.ScopeTypes.Collection, collectionId, adminUserId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId, ["admin"] = true },
            ct);

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
        var userNames = await _userLookup.GetUserNamesAsync(allUserIds, ct);
        var userEmails = await _userLookup.GetUserEmailsAsync(allUserIds, ct);
        var adminIds = await _keycloakUserService.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Admin, ct);

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
