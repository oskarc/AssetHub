using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class BrandEndpoints
{
    /// <summary>1 MB hard cap on logo uploads — enforced server-side too.</summary>
    private const long MaxLogoUploadBytes = 1 * 1024 * 1024;

    public static void MapBrandEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin/brands")
            .RequireAuthorization("RequireAdmin")
            .WithTags("Brands");

        group.MapGet("/", List).WithName("ListBrands");
        group.MapGet("{id:guid}", Get).WithName("GetBrand");
        group.MapPost("/", Create)
            .AddEndpointFilter<ValidationFilter<CreateBrandDto>>()
            .DisableAntiforgery()
            .WithName("CreateBrand");
        group.MapPatch("{id:guid}", Update)
            .AddEndpointFilter<ValidationFilter<UpdateBrandDto>>()
            .DisableAntiforgery()
            .WithName("UpdateBrand");
        group.MapDelete("{id:guid}", Delete)
            .DisableAntiforgery()
            .WithName("DeleteBrand");

        group.MapPost("{id:guid}/logo", UploadLogo)
            .DisableAntiforgery()
            .WithName("UploadBrandLogo");
        group.MapDelete("{id:guid}/logo", RemoveLogo)
            .DisableAntiforgery()
            .WithName("RemoveBrandLogo");

        group.MapPut("{id:guid}/collections/{collectionId:guid}", AssignToCollection)
            .DisableAntiforgery()
            .WithName("AssignBrandToCollection");
        group.MapDelete("collections/{collectionId:guid}", UnassignFromCollection)
            .DisableAntiforgery()
            .WithName("UnassignBrandFromCollection");
    }

    private static async Task<IResult> List(
        [FromServices] IBrandService svc, CancellationToken ct)
        => (await svc.ListAsync(ct)).ToHttpResult();

    private static async Task<IResult> Get(
        Guid id, [FromServices] IBrandService svc, CancellationToken ct)
        => (await svc.GetByIdAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> Create(
        CreateBrandDto dto, [FromServices] IBrandService svc, CancellationToken ct)
        => (await svc.CreateAsync(dto, ct)).ToHttpResult(
            value => Results.Created($"/api/v1/admin/brands/{value.Id}", value));

    private static async Task<IResult> Update(
        Guid id, UpdateBrandDto dto, [FromServices] IBrandService svc, CancellationToken ct)
        => (await svc.UpdateAsync(id, dto, ct)).ToHttpResult();

    private static async Task<IResult> Delete(
        Guid id, [FromServices] IBrandService svc, CancellationToken ct)
        => (await svc.DeleteAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> UploadLogo(
        Guid id,
        [FromForm] IFormFile file,
        [FromServices] IBrandService svc,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "file is required" });
        if (file.Length > MaxLogoUploadBytes)
            return Results.BadRequest(new
            {
                error = $"Logo exceeds the {MaxLogoUploadBytes} byte limit."
            });

        await using var stream = file.OpenReadStream();
        return (await svc.UploadLogoAsync(
            id, stream, file.FileName, file.ContentType ?? "application/octet-stream", ct))
            .ToHttpResult();
    }

    private static async Task<IResult> RemoveLogo(
        Guid id, [FromServices] IBrandService svc, CancellationToken ct)
        => (await svc.RemoveLogoAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> AssignToCollection(
        Guid id, Guid collectionId, [FromServices] IBrandService svc, CancellationToken ct)
        => (await svc.AssignToCollectionAsync(id, collectionId, ct)).ToHttpResult();

    private static async Task<IResult> UnassignFromCollection(
        Guid collectionId, [FromServices] IBrandService svc, CancellationToken ct)
        => (await svc.UnassignFromCollectionAsync(collectionId, ct)).ToHttpResult();
}
