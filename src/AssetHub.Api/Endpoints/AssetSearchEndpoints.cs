using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class AssetSearchEndpoints
{
    public static void MapAssetSearchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets")
            .RequireAuthorization("RequireViewer")
            .WithTags("Asset Search");

        group.MapPost("/search", async (
            [FromBody] AssetSearchRequest request,
            [FromServices] IAssetSearchService svc,
            CancellationToken ct) =>
            (await svc.SearchAsync(request, ct)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<AssetSearchRequest>>()
            .DisableAntiforgery();
    }
}
