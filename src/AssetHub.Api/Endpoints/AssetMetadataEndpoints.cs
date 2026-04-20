using AssetHub.Api.Authentication;
using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Api.OpenApi;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class AssetMetadataEndpoints
{
    public static void MapAssetMetadataEndpoints(this WebApplication app)
    {
        var readGroup = app.MapGroup("/api/v1/assets")
            .RequireAuthorization("RequireViewer")
            .WithTags("Asset Metadata")
            .MarkAsPublicApi();

        readGroup.MapGet("/{id:guid}/metadata", async (
            Guid id,
            [FromServices] IAssetMetadataService svc,
            CancellationToken ct) =>
            (await svc.GetByAssetIdAsync(id, ct)).ToHttpResult())
            .AddEndpointFilter(new RequireScopeFilter("assets:read"));

        // Writes require contributor+; the service additionally checks collection-scoped RBAC.
        var writeGroup = app.MapGroup("/api/v1/assets")
            .RequireAuthorization("RequireContributor")
            .WithTags("Asset Metadata")
            .MarkAsPublicApi();

        var writeScope = new RequireScopeFilter("assets:write");

        writeGroup.MapPut("/{id:guid}/metadata", async (
            Guid id,
            [FromBody] SetAssetMetadataDto dto,
            [FromServices] IAssetMetadataService svc,
            CancellationToken ct) =>
            (await svc.SetAsync(id, dto, ct)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<SetAssetMetadataDto>>()
            .AddEndpointFilter(writeScope)
            .DisableAntiforgery();

        writeGroup.MapPost("/bulk-metadata", async (
            [FromBody] BulkSetAssetMetadataDto dto,
            [FromServices] IAssetMetadataService svc,
            CancellationToken ct) =>
            (await svc.BulkSetAsync(dto, ct)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<BulkSetAssetMetadataDto>>()
            .AddEndpointFilter(writeScope)
            .RequireAuthorization("RequireAdmin")
            .DisableAntiforgery();
    }
}
