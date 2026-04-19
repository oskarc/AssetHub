using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class MetadataSchemaEndpoints
{
    public static void MapMetadataSchemaEndpoints(this WebApplication app)
    {
        // ── Read endpoints (RequireViewer) ──────────────────────────────
        var readGroup = app.MapGroup("/api/v1/metadata-schemas")
            .RequireAuthorization("RequireViewer")
            .WithTags("Metadata Schemas");

        readGroup.MapGet("/", async (
            [FromServices] IMetadataSchemaQueryService svc,
            CancellationToken ct) =>
            (await svc.GetAllAsync(ct)).ToHttpResult());

        readGroup.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] IMetadataSchemaQueryService svc,
            CancellationToken ct) =>
            (await svc.GetByIdAsync(id, ct)).ToHttpResult());

        readGroup.MapGet("/applicable", async (
            [FromQuery] string? assetType,
            [FromQuery] Guid? collectionId,
            [FromServices] IMetadataSchemaQueryService svc,
            CancellationToken ct) =>
            (await svc.GetApplicableAsync(assetType, collectionId, ct)).ToHttpResult());

        // ── Admin endpoints (RequireAdmin) ─────────────────────────────
        var adminGroup = app.MapGroup("/api/v1/admin/metadata-schemas")
            .RequireAuthorization("RequireAdmin")
            .WithTags("Admin - Metadata Schemas");

        adminGroup.MapPost("/", async (
            [FromBody] CreateMetadataSchemaDto dto,
            [FromServices] IMetadataSchemaService svc,
            CancellationToken ct) =>
            (await svc.CreateAsync(dto, ct))
                .ToHttpResult(v => Results.Created($"/api/v1/metadata-schemas/{v.Id}", v)))
            .AddEndpointFilter<ValidationFilter<CreateMetadataSchemaDto>>()
            .DisableAntiforgery();

        adminGroup.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateMetadataSchemaDto dto,
            [FromServices] IMetadataSchemaService svc,
            CancellationToken ct) =>
            (await svc.UpdateAsync(id, dto, ct)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<UpdateMetadataSchemaDto>>()
            .DisableAntiforgery();

        adminGroup.MapDelete("/{id:guid}", async (
            Guid id,
            [FromQuery] bool force,
            [FromServices] IMetadataSchemaService svc,
            CancellationToken ct) =>
            (await svc.DeleteAsync(id, force, ct)).ToHttpResult())
            .DisableAntiforgery();
    }
}
