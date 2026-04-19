using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class TaxonomyEndpoints
{
    public static void MapTaxonomyEndpoints(this WebApplication app)
    {
        // ── Read endpoints (RequireViewer) ──────────────────────────────
        var readGroup = app.MapGroup("/api/v1/taxonomies")
            .RequireAuthorization("RequireViewer")
            .WithTags("Taxonomies");

        readGroup.MapGet("/", async (
            [FromServices] ITaxonomyQueryService svc,
            CancellationToken ct) =>
            (await svc.GetAllAsync(ct)).ToHttpResult());

        readGroup.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] ITaxonomyQueryService svc,
            CancellationToken ct) =>
            (await svc.GetByIdAsync(id, ct)).ToHttpResult());

        // ── Admin endpoints (RequireAdmin) ─────────────────────────────
        var adminGroup = app.MapGroup("/api/v1/admin/taxonomies")
            .RequireAuthorization("RequireAdmin")
            .WithTags("Admin - Taxonomies");

        adminGroup.MapPost("/", async (
            [FromBody] CreateTaxonomyDto dto,
            [FromServices] ITaxonomyService svc,
            CancellationToken ct) =>
            (await svc.CreateAsync(dto, ct))
                .ToHttpResult(v => Results.Created($"/api/v1/taxonomies/{v.Id}", v)))
            .AddEndpointFilter<ValidationFilter<CreateTaxonomyDto>>()
            .DisableAntiforgery();

        adminGroup.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateTaxonomyDto dto,
            [FromServices] ITaxonomyService svc,
            CancellationToken ct) =>
            (await svc.UpdateAsync(id, dto, ct)).ToHttpResult())
            .AddEndpointFilter<ValidationFilter<UpdateTaxonomyDto>>()
            .DisableAntiforgery();

        adminGroup.MapPut("/{id:guid}/terms", async (
            Guid id,
            [FromBody] List<UpsertTaxonomyTermDto> terms,
            [FromServices] ITaxonomyService svc,
            CancellationToken ct) =>
            (await svc.ReplaceTermsAsync(id, terms, ct)).ToHttpResult())
            .DisableAntiforgery();

        adminGroup.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] ITaxonomyService svc,
            CancellationToken ct) =>
            (await svc.DeleteAsync(id, ct)).ToHttpResult())
            .DisableAntiforgery();
    }
}
