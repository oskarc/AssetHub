using AssetHub.Api.Extensions;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class AdminTrashEndpoints
{
    public static void MapAdminTrashEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/trash")
            .RequireAuthorization("RequireAdmin")
            .WithTags("Admin - Trash");

        group.MapGet("/", async (
            [FromQuery] int skip,
            [FromQuery] int take,
            [FromServices] IAssetTrashService svc,
            CancellationToken ct) =>
            (await svc.GetAsync(skip, Math.Clamp(take == 0 ? 50 : take, 1, 200), ct)).ToHttpResult());

        group.MapPost("/{id:guid}/restore", async (
            Guid id,
            [FromServices] IAssetTrashService svc,
            CancellationToken ct) =>
            (await svc.RestoreAsync(id, ct)).ToHttpResult())
            .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] IAssetTrashService svc,
            CancellationToken ct) =>
            (await svc.PurgeAsync(id, ct)).ToHttpResult())
            .DisableAntiforgery();

        group.MapPost("/empty", async (
            [FromServices] IAssetTrashService svc,
            CancellationToken ct) =>
            (await svc.EmptyAsync(ct)).ToHttpResult())
            .DisableAntiforgery();
    }
}
