using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/collections")
            .WithTags("Collections")
            .RequireAuthorization();

        group.MapGet("", GetRootCollections).WithName("GetRootCollections");
        group.MapGet("{id:guid}", GetCollectionById).WithName("GetCollectionById");
        group.MapGet("{id:guid}/deletion-context", GetDeletionContext).WithName("GetCollectionDeletionContext");
        group.MapPost("", CreateCollection).AddEndpointFilter<ValidationFilter<CreateCollectionDto>>().DisableAntiforgery().RequireAuthorization("RequireContributor").WithName("CreateCollection");
        group.MapPatch("{id:guid}", UpdateCollection).AddEndpointFilter<ValidationFilter<UpdateCollectionDto>>().DisableAntiforgery().WithName("UpdateCollection");
        group.MapDelete("{id:guid}", DeleteCollection).DisableAntiforgery().WithName("DeleteCollection");
        group.MapPost("{id:guid}/download-all", DownloadAllAssets).DisableAntiforgery().WithName("DownloadAllAssets");

        // ACL Management
        var aclGroup = app.MapGroup("/api/v1/collections/{collectionId:guid}/acl")
            .WithTags("CollectionACL")
            .RequireAuthorization();

        aclGroup.MapGet("", GetCollectionAcls).WithName("GetCollectionAcls");
        aclGroup.MapPost("", SetCollectionAccess).AddEndpointFilter<ValidationFilter<SetCollectionAccessDto>>().DisableAntiforgery().WithName("SetCollectionAccess");
        aclGroup.MapDelete("{principalType}/{principalId}", RevokeCollectionAccess).DisableAntiforgery().WithName("RevokeCollectionAccess");
        aclGroup.MapGet("/users/search", SearchUsersForAcl).WithName("SearchUsersForAcl");
    }

    // ── Collection CRUD ──────────────────────────────────────────────────────

    private static async Task<IResult> GetRootCollections(
        [FromServices] ICollectionQueryService svc, CancellationToken ct)
    {
        var result = await svc.GetRootCollectionsAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetCollectionById(
        Guid id, [FromServices] ICollectionQueryService svc, CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetDeletionContext(
        Guid id, [FromServices] ICollectionQueryService svc, CancellationToken ct)
    {
        var result = await svc.GetDeletionContextAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateCollection(
        CreateCollectionDto dto,
        [FromServices] ICollectionService svc, CancellationToken ct)
    {
        var result = await svc.CreateAsync(dto, ct);
        return result.ToHttpResult(v => Results.Created($"/api/v1/collections/{v.Id}", v));
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
        return result.ToHttpResult(v => Results.Created($"/api/v1/collections/{collectionId}/acl/{v.Id}", v));
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
