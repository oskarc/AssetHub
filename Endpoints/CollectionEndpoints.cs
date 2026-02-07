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
    }

    private static async Task<IResult> GetRootCollections(
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        var collections = await collectionRepo.GetAccessibleCollectionsAsync(userId, ct);
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
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Check access
        var hasAccess = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer);
        if (!hasAccess)
            return Results.Forbid();

        var collection = await collectionRepo.GetByIdAsync(id, ct: ct);
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
        ClaimsPrincipal user,
        CancellationToken ct)
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
            CreatedAt = DateTime.UtcNow
        };

        await collectionRepo.CreateAsync(collection, ct);

        // Grant creator admin access
        await aclRepo.SetAccessAsync(collection.Id, "user", userId, RoleHierarchy.Roles.Admin);

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
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        dto.ParentId = id;
        return await CreateCollection(dto, collectionRepo, aclRepo, authService, user, ct);
    }

    private static async Task<IResult> UpdateCollection(
        Guid id,
        UpdateCollectionDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Check permission (must be manager)
        var canUpdate = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Manager);
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

        return Results.Ok(new { message = "Collection updated" });
    }

    private static async Task<IResult> DeleteCollection(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Check permission (must be admin)
        var canDelete = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Admin);
        if (!canDelete)
            return Results.Forbid();

        var exists = await collectionRepo.ExistsAsync(id, ct);
        if (!exists)
            return Results.NotFound();

        await collectionRepo.DeleteAsync(id, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> GetChildren(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Check access to parent
        var hasAccess = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer);
        if (!hasAccess)
            return Results.Forbid();

        var children = await collectionRepo.GetChildrenAsync(id, ct);
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

        if (!RoleHierarchy.AllRoles.Contains(dto.Role))
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

    private static async Task<IResult> DownloadAllAssets(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = user.GetUserId();
        if (userId == null)
            return Results.Unauthorized();

        // Check viewer access
        var canView = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer);
        if (!canView)
            return Results.Forbid();

        var collection = await collectionRepo.GetByIdAsync(id, ct: ct);
        if (collection == null)
            return Results.NotFound("Collection not found");

        var assets = await assetRepo.GetByCollectionAsync(id, 0, 1000, ct);
        if (!assets.Any())
            return Results.BadRequest("No assets in collection");

        var bucketName = StorageConfig.GetBucketName(configuration);

        // Create ZIP in memory
        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            foreach (var asset in assets.Where(a => !string.IsNullOrEmpty(a.OriginalObjectKey)))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var assetStream = await minioAdapter.DownloadAsync(bucketName, asset.OriginalObjectKey!, ct);
                    var fileName = FileHelpers.GetSafeFileName(asset.Title, asset.OriginalObjectKey!, asset.ContentType);
                    
                    var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    await assetStream.CopyToAsync(entryStream, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Skip assets that fail to download
                }
            }
        }

        memoryStream.Position = 0;
        var zipFileName = $"{collection.Name.Replace(" ", "_")}_assets.zip";
        return Results.File(memoryStream, "application/zip", zipFileName);
    }

}
