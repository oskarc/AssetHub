using System.Security.Claims;
using Dam.Application.Dtos;
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
    }

    private static async Task<IResult> GetRootCollections(
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        var collections = await collectionRepo.GetAccessibleCollectionsAsync(userId);
        var dtos = collections.Select(c => new CollectionResponseDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            ParentId = c.ParentId,
            CreatedAt = c.CreatedAt,
            CreatedByUserId = c.CreatedByUserId
        });

        return Results.Ok(dtos);
    }

    private static async Task<IResult> GetCollectionById(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Check access
        var hasAccess = await authService.CheckAccessAsync(userId, id, "viewer");
        if (!hasAccess)
            return Results.Forbid();

        var collection = await collectionRepo.GetByIdAsync(id);
        if (collection == null)
            return Results.NotFound();

        var userRole = await authService.GetUserRoleAsync(userId, id);
        var dto = new CollectionResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            ParentId = collection.ParentId,
            CreatedAt = collection.CreatedAt,
            CreatedByUserId = collection.CreatedByUserId,
            UserRole = userRole ?? "none"
        };

        return Results.Ok(dto);
    }

    private static async Task<IResult> CreateCollection(
        CreateCollectionDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Validate name
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 255)
            return Results.BadRequest("Name must be 1-255 characters");

        // Check permission
        if (dto.ParentId.HasValue)
        {
            var canCreate = await authService.CanCreateSubCollectionAsync(userId, dto.ParentId.Value);
            if (!canCreate)
                return Results.Forbid();
        }
        else
        {
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
            CreatedAt = DateTime.Now
        };

        await collectionRepo.CreateAsync(collection);

        // Grant creator admin access
        await aclRepo.SetAccessAsync(collection.Id, "user", userId, "admin");

        var responseDto = new CollectionResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            ParentId = collection.ParentId,
            CreatedAt = collection.CreatedAt,
            CreatedByUserId = collection.CreatedByUserId,
            UserRole = "admin"
        };

        return Results.Created($"/api/collections/{collection.Id}", responseDto);
    }

    private static async Task<IResult> CreateSubCollection(
        Guid id,
        CreateCollectionDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user)
    {
        dto.ParentId = id;
        return await CreateCollection(dto, collectionRepo, aclRepo, authService, user);
    }

    private static async Task<IResult> UpdateCollection(
        Guid id,
        UpdateCollectionDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Check permission (must be manager)
        var canUpdate = await authService.CheckAccessAsync(userId, id, "manager");
        if (!canUpdate)
            return Results.Forbid();

        var collection = await collectionRepo.GetByIdAsync(id);
        if (collection == null)
            return Results.NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Name))
            collection.Name = dto.Name;
        if (dto.Description != null)
            collection.Description = dto.Description;

        await collectionRepo.UpdateAsync(collection);

        return Results.Ok(new { message = "Collection updated" });
    }

    private static async Task<IResult> DeleteCollection(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Check permission (must be admin)
        var canDelete = await authService.CheckAccessAsync(userId, id, "admin");
        if (!canDelete)
            return Results.Forbid();

        var exists = await collectionRepo.ExistsAsync(id);
        if (!exists)
            return Results.NotFound();

        await collectionRepo.DeleteAsync(id);

        return Results.NoContent();
    }

    private static async Task<IResult> GetChildren(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Check access to parent
        var hasAccess = await authService.CheckAccessAsync(userId, id, "viewer");
        if (!hasAccess)
            return Results.Forbid();

        var children = await collectionRepo.GetChildrenAsync(id);
        var dtos = children.Select(c => new CollectionResponseDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            ParentId = c.ParentId,
            CreatedAt = c.CreatedAt,
            CreatedByUserId = c.CreatedByUserId
        });

        return Results.Ok(dtos);
    }

    private static async Task<IResult> GetCollectionAcls(
        Guid collectionId,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Must have manager+ access
        var canManage = await authService.CanManageAclAsync(userId, collectionId);
        if (!canManage)
            return Results.Forbid();

        var acls = await aclRepo.GetByCollectionAsync(collectionId);
        var dtos = acls.Select(a => new CollectionAclResponseDto
        {
            Id = a.Id,
            PrincipalType = a.PrincipalType,
            PrincipalId = a.PrincipalId,
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
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Must have manager+ access
        var canManage = await authService.CanManageAclAsync(userId, collectionId);
        if (!canManage)
            return Results.Forbid();

        // Validate
        if (string.IsNullOrWhiteSpace(dto.PrincipalType) || string.IsNullOrWhiteSpace(dto.PrincipalId) || string.IsNullOrWhiteSpace(dto.Role))
            return Results.BadRequest("PrincipalType, PrincipalId, and Role are required");

        if (!new[] { "viewer", "contributor", "manager", "admin" }.Contains(dto.Role))
            return Results.BadRequest("Invalid role");

        var acl = await aclRepo.SetAccessAsync(collectionId, dto.PrincipalType, dto.PrincipalId, dto.Role);

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
        ClaimsPrincipal user)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Must have manager+ access
        var canManage = await authService.CanManageAclAsync(userId, collectionId);
        if (!canManage)
            return Results.Forbid();

        await aclRepo.RevokeAccessAsync(collectionId, principalType, principalId);

        return Results.NoContent();
    }
}
