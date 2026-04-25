using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class SavedSearchEndpoints
{
    public static void MapSavedSearchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/saved-searches")
            .RequireAuthorization("RequireViewer")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Saved Searches");

        group.MapGet("/", async (
            [FromServices] ISavedSearchService svc,
            CancellationToken ct) =>
            (await svc.GetMineAsync(ct)).ToHttpResult());

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] ISavedSearchService svc,
            CancellationToken ct) =>
            (await svc.GetByIdAsync(id, ct)).ToHttpResult());

        group.MapPost("/", async (
            [FromBody] CreateSavedSearchDto dto,
            [FromServices] ISavedSearchService svc,
            CancellationToken ct) =>
            (await svc.CreateAsync(dto, ct))
                .ToHttpResult(v => Results.Created($"/api/v1/saved-searches/{v.Id}", v)))
            .AddEndpointFilter<ValidationFilter<CreateSavedSearchDto>>()
            .DisableAntiforgery();

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateSavedSearchDto dto,
            [FromServices] ISavedSearchService svc,
            CancellationToken ct) =>
            (await svc.UpdateAsync(id, dto, ct)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<UpdateSavedSearchDto>>()
            .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] ISavedSearchService svc,
            CancellationToken ct) =>
            (await svc.DeleteAsync(id, ct)).ToHttpResult())
            .DisableAntiforgery();
    }
}
