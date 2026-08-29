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
            .RequireAuthorization()
            .RequireAntiforgeryUnlessBearer();

        // The group-level RequireAntiforgeryUnlessBearer() is the CSRF gate for cookie
        // principals; Bearer clients are inherently CSRF-immune.
        // deletion-context is a UI-specific pre-delete preview — kept internal.
        // download-all kicks off a ZIP build job and streams a UI-driven download flow — kept internal.
        group.MapPost("{id:guid}/download-all", DownloadAllAssets).DisableAntiforgery().WithName("DownloadAllAssets");

        // ACL Management — admin/manager UX surface, not part of the public integration contract.
        var aclGroup = app.MapGroup("/api/v1/collections/{collectionId:guid}/acl")
            .WithTags("CollectionACL")
            .RequireAuthorization()
            .RequireAntiforgeryUnlessBearer();

    }

    // ── Collection CRUD ──────────────────────────────────────────────────────







    private static async Task<IResult> DownloadAllAssets(
        Guid id, [FromServices] ICollectionService svc,
        CancellationToken ct)
    {
        var result = await svc.DownloadAllAssetsAsync(id, ct);
        return result.ToHttpResult(v => Results.Accepted(v.StatusUrl, v));
    }

    // ── ACL Management ───────────────────────────────────────────────────────




}
