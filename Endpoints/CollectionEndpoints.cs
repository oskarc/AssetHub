using System.IO.Compression;
using System.Security.Claims;
using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AssetHub.Endpoints;

public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/collections")
            .WithName("Collections")
            .DisableAntiforgery() // API uses JWT Bearer auth, not cookies with CSRF tokens
            .RequireAuthorization();

        group.MapGet("", GetRootCollections)
            .WithName("GetRootCollections");

        group.MapGet("{id}", GetCollectionById)
            .WithName("GetCollectionById");

        group.MapPost("", CreateCollection)
            .WithName("CreateCollection");

        group.MapPost("{id}/children", CreateSubCollection)
            .WithName("CreateSubCollection");

        group.MapPatch("{id}", UpdateCollection)
            .WithName("UpdateCollection");

        group.MapDelete("{id}", DeleteCollection)
            .WithName("DeleteCollection");

        group.MapGet("{id}/children", GetChildren)
            .WithName("GetChildren");

        group.MapGet("{id}/download-all", DownloadAllAssets)
            .WithName("DownloadAllAssets");

        // ACL Management
        var aclGroup = app.MapGroup("/api/collections/{collectionId}/acl")
            .WithName("CollectionACL")
            .RequireAuthorization();

        aclGroup.MapGet("", GetCollectionAcls)
            .WithName("GetCollectionAcls");

        aclGroup.MapPost("", SetCollectionAccess)
            .WithName("SetCollectionAccess");

        aclGroup.MapDelete("{principalType}/{principalId}", RevokeCollectionAccess)
            .WithName("RevokeCollectionAccess");

        // User search for ACL management (managers can search for users to grant access)
        aclGroup.MapGet("/users/search", SearchUsersForAcl)
            .WithName("SearchUsersForAcl");
    }

    private static async Task<IResult> GetRootCollections(
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        var collections = await collectionRepo.GetAccessibleCollectionsAsync(userId, ct);

        // Build a set of all accessible IDs so we can identify "entry-point" collections:
        // those whose parent is either null (root) or not in the accessible set.
        // This avoids flooding the flat sidebar with deeply nested children.
        var accessibleIds = collections.Select(c => c.Id).ToHashSet();

        var entryPoints = collections
            .Where(c => c.ParentId == null || !accessibleIds.Contains(c.ParentId.Value));

        var dtos = await CollectionMapper.ToDtoListAsync(entryPoints, userId, authService, ct);
        return Results.Ok(dtos);
    }

    private static async Task<IResult> GetCollectionById(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        // Check access
        var hasAccess = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer, ct);
        if (!hasAccess)
            return Results.Forbid();

        var collection = await collectionRepo.GetByIdAsync(id, ct: ct);
        if (collection == null)
            return Results.NotFound();

        var dto = await CollectionMapper.ToDtoAsync(collection, userId, authService, ct);
        return Results.Ok(dto);
    }

    private static async Task<IResult> CreateCollection(
        CreateCollectionDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IAuditService audit,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        // Validate name
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 255)
            return Results.BadRequest("Name must be 1-255 characters");

        // Check permission
        if (dto.ParentId.HasValue)
        {
            var canCreate = await authService.CanCreateSubCollectionAsync(userId, dto.ParentId.Value, ct);
            if (!canCreate)
                return Results.Forbid();
        }
        else
        {
            // Root collection creation requires at least manager role
            if (!user.IsInRole(RoleHierarchy.Roles.Manager) && !user.IsInRole(RoleHierarchy.Roles.Admin))
                return Results.Forbid();

            var canCreate = await authService.CanCreateRootCollectionAsync(userId);
            if (!canCreate)
                return Results.Forbid();
        }

        // Create collection
        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            ParentId = dto.ParentId,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        await collectionRepo.CreateAsync(collection, ct);

        // Grant creator admin access
        await aclRepo.SetAccessAsync(collection.Id, "user", userId, RoleHierarchy.Roles.Admin, ct);

        await audit.LogAsync("collection.created", "collection", collection.Id, userId,
            new() { ["name"] = collection.Name, ["parentId"] = (object?)collection.ParentId ?? "root" }, httpContext, ct);

        var responseDto = new CollectionResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            ParentId = collection.ParentId,
            CreatedAt = collection.CreatedAt,
            CreatedByUserId = collection.CreatedByUserId,
            UserRole = RoleHierarchy.Roles.Admin
        };

        return Results.Created($"/api/collections/{collection.Id}", responseDto);
    }

    private static async Task<IResult> CreateSubCollection(
        Guid id,
        CreateCollectionDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IAuditService audit,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        dto.ParentId = id;
        return await CreateCollection(dto, collectionRepo, aclRepo, authService, audit, user, httpContext, ct);
    }

    private static async Task<IResult> UpdateCollection(
        Guid id,
        UpdateCollectionDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IAuditService audit,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        // Check permission (must be manager)
        var canUpdate = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Manager, ct);
        if (!canUpdate)
            return Results.Forbid();

        var collection = await collectionRepo.GetByIdAsync(id, ct: ct);
        if (collection == null)
            return Results.NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Name))
            collection.Name = dto.Name;
        if (dto.Description != null)
            collection.Description = dto.Description;

        await collectionRepo.UpdateAsync(collection, ct);

        await audit.LogAsync("collection.updated", "collection", id, userId, httpContext: httpContext, ct: ct);

        return Results.Ok(new MessageResponse("Collection updated"));
    }

    private static async Task<IResult> DeleteCollection(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetDeletionService deletionService,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IAuditService audit,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        var canDelete = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Admin, ct);
        if (!canDelete)
            return Results.Forbid();

        var exists = await collectionRepo.ExistsAsync(id, ct);
        if (!exists)
            return Results.NotFound();

        var bucketName = StorageConfig.GetBucketName(configuration);
        await deletionService.DeleteCollectionAssetsAsync(id, bucketName, ct);

        await collectionRepo.DeleteAsync(id, ct);

        await audit.LogAsync("collection.deleted", "collection", id, userId, httpContext: httpContext, ct: ct);

        return Results.NoContent();
    }

    private static async Task<IResult> GetChildren(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        // Check access to parent
        var hasAccess = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer, ct);
        if (!hasAccess)
            return Results.Forbid();

        var children = await collectionRepo.GetChildrenAsync(id, ct);
        var dtos = await CollectionMapper.ToDtoListAsync(children, userId, authService, ct);
        return Results.Ok(dtos);
    }

    private static async Task<IResult> GetCollectionAcls(
        Guid collectionId,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IUserLookupService userLookup,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        // Must have manager+ access
        var canManage = await authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return Results.Forbid();

        var acls = await aclRepo.GetByCollectionAsync(collectionId, ct);

        // Resolve user names for all user principals
        var userIds = acls.Where(a => a.PrincipalType == "user").Select(a => a.PrincipalId);
        var nameMap = await userLookup.GetUserNamesAsync(userIds, ct);

        var dtos = acls.Select(a => new CollectionAclResponseDto
        {
            Id = a.Id,
            PrincipalType = a.PrincipalType,
            PrincipalId = a.PrincipalId,
            PrincipalName = a.PrincipalType == "user" && nameMap.TryGetValue(a.PrincipalId, out var name) ? name
                : a.PrincipalType == "user" ? $"Deleted User ({a.PrincipalId[..Math.Min(8, a.PrincipalId.Length)]})" : null,
            Role = a.Role,
            CreatedAt = a.CreatedAt
        });

        return Results.Ok(dtos);
    }

    private static async Task<IResult> SetCollectionAccess(
        Guid collectionId,
        SetCollectionAccessDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IAuditService audit,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        // Must have manager+ access
        var canManage = await authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return Results.Forbid();

        // Validate
        if (string.IsNullOrWhiteSpace(dto.PrincipalType) || string.IsNullOrWhiteSpace(dto.PrincipalId) || string.IsNullOrWhiteSpace(dto.Role))
            return Results.BadRequest("PrincipalType, PrincipalId, and Role are required");

        if (!RoleHierarchy.AllRoles.Contains(dto.Role))
            return Results.BadRequest("Invalid role");

        // Role escalation guard: caller can only grant roles at or below their own level
        var callerRole = await authService.GetUserRoleAsync(userId, collectionId, ct);
        if (!RoleHierarchy.CanGrantRole(callerRole, dto.Role))
            return Results.BadRequest($"You cannot grant the '{dto.Role}' role because it exceeds your own access level");

        var acl = await aclRepo.SetAccessAsync(collectionId, dto.PrincipalType, dto.PrincipalId, dto.Role, ct);

        await audit.LogAsync("acl.set", "collection", collectionId, userId,
            new() { ["principalType"] = dto.PrincipalType, ["principalId"] = dto.PrincipalId, ["role"] = dto.Role }, httpContext, ct);

        var responseDto = new CollectionAclResponseDto
        {
            Id = acl.Id,
            PrincipalType = acl.PrincipalType,
            PrincipalId = acl.PrincipalId,
            Role = acl.Role,
            CreatedAt = acl.CreatedAt
        };

        return Results.Created($"/api/collections/{collectionId}/acl/{acl.Id}", responseDto);
    }

    private static async Task<IResult> RevokeCollectionAccess(
        Guid collectionId,
        string principalType,
        string principalId,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IAuditService audit,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        // Must have manager+ access
        var canManage = await authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return Results.Forbid();

        // Role escalation guard: caller can only revoke roles at or below their own level
        var callerRole = await authService.GetUserRoleAsync(userId, collectionId, ct);
        var targetAcl = await aclRepo.GetByPrincipalAsync(collectionId, principalType, principalId, ct);
        if (targetAcl != null && !RoleHierarchy.CanRevokeRole(callerRole, targetAcl.Role))
            return Results.BadRequest($"You cannot revoke a '{targetAcl.Role}' role because it exceeds your own access level");

        await aclRepo.RevokeAccessAsync(collectionId, principalType, principalId, ct);

        await audit.LogAsync("acl.revoked", "collection", collectionId, userId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId }, httpContext, ct);

        return Results.NoContent();
    }

    /// <summary>
    /// Searches for users that can be added to a collection's ACL.
    /// Requires manager+ access to the collection. Returns basic user info.
    /// </summary>
    private static async Task<IResult> SearchUsersForAcl(
        Guid collectionId,
        [Microsoft.AspNetCore.Mvc.FromQuery] string? q,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IUserLookupService userLookup,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        var canManage = await authService.CanManageAclAsync(userId, collectionId, ct);
        if (!canManage)
            return Results.Forbid();

        var allUsers = await userLookup.GetAllUsersAsync(ct);

        // Filter by query if provided
        if (!string.IsNullOrWhiteSpace(q))
        {
            var query = q.Trim();
            allUsers = allUsers
                .Where(u => u.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (u.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        // Exclude users who already have direct access
        var existingAcls = await aclRepo.GetByCollectionAsync(collectionId, ct);
        var existingUserIds = existingAcls
            .Where(a => a.PrincipalType == "user")
            .Select(a => a.PrincipalId)
            .ToHashSet();

        var result = allUsers
            .Where(u => !existingUserIds.Contains(u.Id))
            .Select(u => new UserSearchResultDto { Id = u.Id, Username = u.Username, Email = u.Email })
            .OrderBy(u => u.Username)
            .Take(50)
            .ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> DownloadAllAssets(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IZipDownloadService zipService,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = user.GetRequiredUserId();

        var canView = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer, ct);
        if (!canView)
            return Results.Forbid();

        var collection = await collectionRepo.GetByIdAsync(id, ct: ct);
        if (collection == null)
            return Results.NotFound("Collection not found");

        var assets = await assetRepo.GetByCollectionAsync(id, 0, 1000, ct);
        if (!assets.Any())
            return Results.BadRequest("No assets in collection");

        var bucketName = StorageConfig.GetBucketName(configuration);
        var zipFileName = $"{collection.Name.Replace(" ", "_")}_assets.zip";

        await zipService.StreamAssetsAsZipAsync(assets, bucketName, zipFileName, httpContext, ct);

        return Results.Empty;
    }

}
