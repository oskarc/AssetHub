using AssetHub.Extensions;
using Dam.Application.Dtos;
using Dam.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Endpoints;

public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/collections")
            .WithTags("Collections")
            .RequireAuthorization();

        group.MapGet("", GetRootCollections).WithName("GetRootCollections");
        group.MapGet("{id:guid}", GetCollectionById).WithName("GetCollectionById");
        group.MapPost("", CreateCollection).WithName("CreateCollection");
        group.MapPost("{id:guid}/children", CreateSubCollection).WithName("CreateSubCollection");
        group.MapPatch("{id:guid}", UpdateCollection).WithName("UpdateCollection");
        group.MapDelete("{id:guid}", DeleteCollection).WithName("DeleteCollection");
        group.MapGet("{id:guid}/children", GetChildren).WithName("GetChildren");
        group.MapPost("{id:guid}/download-all", DownloadAllAssets).WithName("DownloadAllAssets");

        // ACL Management
        var aclGroup = app.MapGroup("/api/collections/{collectionId:guid}/acl")
            .WithTags("CollectionACL")
            .RequireAuthorization();

        aclGroup.MapGet("", GetCollectionAcls).WithName("GetCollectionAcls");
        aclGroup.MapPost("", SetCollectionAccess).WithName("SetCollectionAccess");
        aclGroup.MapDelete("{principalType}/{principalId}", RevokeCollectionAccess).WithName("RevokeCollectionAccess");
        aclGroup.MapGet("/users/search", SearchUsersForAcl).WithName("SearchUsersForAcl");
    }

    // ── Collection CRUD ──────────────────────────────────────────────────────

    private static async Task<IResult> GetRootCollections(
        [FromServices] ICollectionService svc, CancellationToken ct)
    {
        var result = await svc.GetRootCollectionsAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetCollectionById(
        Guid id, [FromServices] ICollectionService svc, CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateCollection(
        CreateCollectionDto dto,
        [FromServices] ICollectionService svc, CancellationToken ct)
    {
        var result = await svc.CreateAsync(dto, ct);
        return result.ToHttpResult(v => Results.Created($"/api/collections/{v.Id}", v));
    }

    private static async Task<IResult> CreateSubCollection(
        Guid id, CreateCollectionDto dto,
        [FromServices] ICollectionService svc, CancellationToken ct)
    {
        dto.ParentId = id;
        var result = await svc.CreateAsync(dto, ct);
        return result.ToHttpResult(v => Results.Created($"/api/collections/{v.Id}", v));
    }

    private static async Task<IResult> UpdateCollection(
        Guid id, UpdateCollectionDto dto,
        [FromServices] ICollectionService svc, CancellationToken ct)
    {
        var result = await svc.UpdateAsync(id, dto, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteCollection(
        Guid id, [FromServices] ICollectionService svc, CancellationToken ct)
    {
        var result = await svc.DeleteAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetChildren(
        Guid id, [FromServices] ICollectionService svc, CancellationToken ct)
    {
        var result = await svc.GetChildrenAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DownloadAllAssets(
        Guid id, [FromServices] ICollectionService svc,
        CancellationToken ct)
    {
        var result = await svc.DownloadAllAssetsAsync(id, ct);
        return result.ToHttpResult(v => Results.Accepted(v.StatusUrl, v));
    }

    // ── ACL Management ───────────────────────────────────────────────────────

    private static async Task<IResult> GetCollectionAcls(
        Guid collectionId, [FromServices] ICollectionAclService svc, CancellationToken ct)
    {
        var result = await svc.GetAclsAsync(collectionId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SetCollectionAccess(
        Guid collectionId, SetCollectionAccessDto dto,
        [FromServices] ICollectionAclService svc, CancellationToken ct)
    {
        var result = await svc.SetAccessAsync(collectionId, dto.PrincipalType, dto.PrincipalId, dto.Role, ct);
        return result.ToHttpResult(v => Results.Created($"/api/collections/{collectionId}/acl/{v.Id}", v));
    }

    private static async Task<IResult> RevokeCollectionAccess(
        Guid collectionId, string principalType, string principalId,
        [FromServices] ICollectionAclService svc, CancellationToken ct)
    {
        var result = await svc.RevokeAccessAsync(collectionId, principalType, principalId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SearchUsersForAcl(
        Guid collectionId, [FromQuery] string? q,
        [FromServices] ICollectionAclService svc, CancellationToken ct)
    {
        var result = await svc.SearchUsersForAclAsync(collectionId, q, ct);
        return result.ToHttpResult();
    }
}
