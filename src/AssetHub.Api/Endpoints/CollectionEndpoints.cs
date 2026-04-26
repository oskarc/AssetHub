using AssetHub.Api.Authentication;
using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Api.OpenApi;
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
            .RequireAuthorization()
            .RequireAntiforgeryUnlessBearer();

        var read = new RequireScopeFilter("collections:read");
        var write = new RequireScopeFilter("collections:write");

        group.MapGet("", GetRootCollections).AddEndpointFilter(read).WithName("GetRootCollections").MarkAsPublicApi();
        group.MapGet("{id:guid}", GetCollectionById).AddEndpointFilter(read).WithName("GetCollectionById").MarkAsPublicApi();
        // deletion-context is a UI-specific pre-delete preview — kept internal.
        group.MapGet("{id:guid}/deletion-context", GetDeletionContext).WithName("GetCollectionDeletionContext");
        group.MapPost("", CreateCollection).AddEndpointFilter<ValidationFilter<CreateCollectionDto>>().AddEndpointFilter(write).DisableAntiforgery().RequireAuthorization("RequireContributor").WithName("CreateCollection").MarkAsPublicApi();
        group.MapPatch("{id:guid}", UpdateCollection).AddEndpointFilter<ValidationFilter<UpdateCollectionDto>>().AddEndpointFilter(write).DisableAntiforgery().WithName("UpdateCollection").MarkAsPublicApi();
        group.MapDelete("{id:guid}", DeleteCollection).AddEndpointFilter(write).DisableAntiforgery().WithName("DeleteCollection").MarkAsPublicApi();
        // download-all kicks off a ZIP build job and streams a UI-driven download flow — kept internal.
        group.MapPost("{id:guid}/download-all", DownloadAllAssets).DisableAntiforgery().WithName("DownloadAllAssets");

        // Nested collections (T5-NEST-01) — admin-only mutations of parent / inheritance.
        // Reparent + inherit toggle are public-API ("collections:write") so admins can script
        // taxonomy setup. Copy-from-parent stays internal — admin UX only, not part of the contract.
        group.MapPatch("{id:guid}/parent", SetCollectionParent)
            .AddEndpointFilter<ValidationFilter<SetParentRequestDto>>()
            .AddEndpointFilter(write)
            .DisableAntiforgery()
            .RequireAuthorization("RequireAdmin")
            .WithName("SetCollectionParent")
            .MarkAsPublicApi();
        group.MapPatch("{id:guid}/inherit-acl", SetCollectionInheritAcl)
            .AddEndpointFilter<ValidationFilter<SetInheritAclRequestDto>>()
            .AddEndpointFilter(write)
            .DisableAntiforgery()
            .RequireAuthorization("RequireAdmin")
            .WithName("SetCollectionInheritAcl")
            .MarkAsPublicApi();
        group.MapPost("{id:guid}/copy-acl-from-parent", CopyCollectionAclFromParent)
            .DisableAntiforgery()
            .RequireAuthorization("RequireAdmin")
            .WithName("CopyCollectionAclFromParent");

        // ACL Management — admin/manager UX surface, not part of the public integration contract.
        var aclGroup = app.MapGroup("/api/v1/collections/{collectionId:guid}/acl")
            .WithTags("CollectionACL")
            .RequireAuthorization()
            .RequireAntiforgeryUnlessBearer();

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

    // ── Nested collections (T5-NEST-01) ──────────────────────────────────────

    private static async Task<IResult> SetCollectionParent(
        Guid id, SetParentRequestDto dto,
        [FromServices] ICollectionService svc, CancellationToken ct)
    {
        var result = await svc.SetParentAsync(id, dto.ParentId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SetCollectionInheritAcl(
        Guid id, SetInheritAclRequestDto dto,
        [FromServices] ICollectionService svc, CancellationToken ct)
    {
        // [Required] on a bool? guarantees non-null by the validation filter.
        var result = await svc.SetInheritParentAclAsync(id, dto.Inherit!.Value, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CopyCollectionAclFromParent(
        Guid id, [FromServices] ICollectionService svc, CancellationToken ct)
    {
        var result = await svc.CopyParentAclAsync(id, ct);
        return result.ToHttpResult(count => Results.Ok(new CopyParentAclResponseDto(count)));
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
