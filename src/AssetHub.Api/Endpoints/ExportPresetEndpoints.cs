using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

/// <summary>
/// Admin-only CRUD endpoints for managing export presets.
/// </summary>
public static class ExportPresetEndpoints
{
    public static void MapExportPresetEndpoints(this WebApplication app)
    {
        // Read-only access for all authenticated users (contributors use this in the image editor)
        var readGroup = app.MapGroup("/api/v1/export-presets")
            .RequireAuthorization("RequireViewer")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("ExportPresets");

        readGroup.MapGet("/", GetAll).WithName("GetExportPresets");

        // Admin-only CRUD
        var adminGroup = app.MapGroup("/api/v1/admin/export-presets")
            .RequireAuthorization("RequireAdmin")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("ExportPresets");

        adminGroup.MapGet("/{id:guid}", GetById).WithName("GetExportPresetById");
        adminGroup.MapPost("/", Create)
            .AddEndpointFilter<ValidationFilter<CreateExportPresetDto>>()
            .DisableAntiforgery()
            .WithName("CreateExportPreset");
        adminGroup.MapPatch("/{id:guid}", Update)
            .AddEndpointFilter<ValidationFilter<UpdateExportPresetDto>>()
            .DisableAntiforgery()
            .WithName("UpdateExportPreset");
        adminGroup.MapDelete("/{id:guid}", Delete)
            .DisableAntiforgery()
            .WithName("DeleteExportPreset");
    }

    private static async Task<IResult> GetAll(
        [FromServices] IExportPresetQueryService svc, CancellationToken ct)
    {
        var result = await svc.GetAllAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetById(
        Guid id, [FromServices] IExportPresetQueryService svc, CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Create(
        [FromBody] CreateExportPresetDto dto,
        [FromServices] IExportPresetService svc, CancellationToken ct)
    {
        var result = await svc.CreateAsync(dto, ct);
        return result.ToHttpResult(v => Results.Created($"/api/v1/admin/export-presets/{v.Id}", v));
    }

    private static async Task<IResult> Update(
        Guid id, [FromBody] UpdateExportPresetDto dto,
        [FromServices] IExportPresetService svc, CancellationToken ct)
    {
        var result = await svc.UpdateAsync(id, dto, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Delete(
        Guid id, [FromServices] IExportPresetService svc, CancellationToken ct)
    {
        var result = await svc.DeleteAsync(id, ct);
        return result.ToHttpResult();
    }
}
