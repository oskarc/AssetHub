using AssetHub.Api.Extensions;
using AssetHub.Api.OpenApi;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class AssetVersionEndpoints
{
    public static void MapAssetVersionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets/{id:guid}/versions")
            .RequireAuthorization("RequireViewer")
            .WithTags("Asset Versions")
            .MarkAsPublicApi();

        group.MapGet("/", async (
            Guid id,
            [FromServices] IAssetVersionService svc,
            CancellationToken ct) =>
            (await svc.GetForAssetAsync(id, ct)).ToHttpResult());

        // Restore is a Contributor-level mutation (the service double-checks RBAC).
        group.MapPost("/{n:int}/restore", async (
            Guid id,
            int n,
            [FromServices] IAssetVersionService svc,
            CancellationToken ct) =>
            (await svc.RestoreAsync(id, n, ct)).ToHttpResult())
            .DisableAntiforgery();

        // Prune permanently removes a single version (admin only — service enforces).
        group.MapDelete("/{n:int}", async (
            Guid id,
            int n,
            [FromServices] IAssetVersionService svc,
            CancellationToken ct) =>
            (await svc.PruneAsync(id, n, ct)).ToHttpResult())
            .DisableAntiforgery();
    }
}
