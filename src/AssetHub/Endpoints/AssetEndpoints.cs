using AssetHub.Extensions;
using Dam.Application.Dtos;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Endpoints;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/assets")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithTags("Assets");

        group.MapGet("", GetAssets).RequireAuthorization("RequireAdmin").WithName("GetAssets");
        group.MapGet("all", GetAllAssets).RequireAuthorization("RequireAdmin").WithName("GetAllAssets");
        group.MapGet("{id}", GetAsset).WithName("GetAsset");
        group.MapPost("", UploadAsset).WithName("UploadAsset");
        group.MapPatch("{id}", UpdateAsset).WithName("UpdateAsset");
        group.MapDelete("{id}", DeleteAsset).WithName("DeleteAsset");
        group.MapGet("collection/{collectionId}", GetAssetsByCollection).WithName("GetAssetsByCollection");

        group.MapGet("{id}/collections", GetAssetCollections).WithName("GetAssetCollections");
        group.MapPost("{id}/collections/{collectionId}", AddAssetToCollection).WithName("AddAssetToCollection");
        group.MapDelete("{id}/collections/{collectionId}", RemoveAssetFromCollection).WithName("RemoveAssetFromCollection");
        group.MapGet("{id}/deletion-context", GetAssetDeletionContext).WithName("GetAssetDeletionContext");

        group.MapPost("init-upload", InitUpload).WithName("InitUpload");
        group.MapPost("{id}/confirm-upload", ConfirmUpload).WithName("ConfirmUpload");

        group.MapGet("{id}/download", GetRendition("original")).WithName("DownloadOriginal");
        group.MapGet("{id}/preview", GetRendition("original")).WithName("PreviewOriginal");
        group.MapGet("{id}/thumb", GetRendition("thumb")).WithName("GetThumbnail");
        group.MapGet("{id}/medium", GetRendition("medium")).WithName("GetMedium");
        group.MapGet("{id}/poster", GetRendition("poster")).WithName("GetPoster");
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    private static async Task<IResult> GetAssets(
        [FromServices] IAssetService svc, CancellationToken ct,
        int skip = 0, int take = 50)
    {
        var result = await svc.GetAssetsByStatusAsync(Asset.StatusReady, skip, take, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAllAssets(
        [FromServices] IAssetService svc, CancellationToken ct,
        string? query = null, string? type = null, Guid? collectionId = null,
        string sortBy = "created_desc", int skip = 0, int take = 50)
    {
        var result = await svc.GetAllAssetsAsync(query, type, collectionId, sortBy, skip, take, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAsset(
        Guid id, [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.GetAssetAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAssetsByCollection(
        Guid collectionId, [FromServices] IAssetService svc, CancellationToken ct,
        string? query = null, string? type = null,
        string sortBy = "created_desc", int skip = 0, int take = 50)
    {
        var result = await svc.GetAssetsByCollectionAsync(collectionId, query, type, sortBy, skip, take, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAssetDeletionContext(
        Guid id, [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.GetDeletionContextAsync(id, ct);
        return result.ToHttpResult();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    private static async Task<IResult> UploadAsset(
        IFormFile file, [FromForm] Guid collectionId, [FromForm] string title,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { error = "File is required" });

        using var stream = file.OpenReadStream();
        var result = await svc.UploadAsync(stream, file.FileName, file.ContentType, file.Length, collectionId, title, ct);
        return result.ToHttpResult(v => Results.Accepted($"/api/assets/{v.Id}", v));
    }

    private static async Task<IResult> UpdateAsset(
        Guid id, UpdateAssetDto dto,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.UpdateAsync(id, dto, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteAsset(
        Guid id, [FromQuery] Guid? fromCollectionId, [FromQuery] bool permanent,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.DeleteAsync(id, fromCollectionId, permanent, ct);
        return result.ToHttpResult();
    }

    // ── Presigned Upload ─────────────────────────────────────────────────────

    private static async Task<IResult> InitUpload(
        InitUploadRequest request,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.InitUploadAsync(request, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ConfirmUpload(
        Guid id, [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.ConfirmUploadAsync(id, ct);
        return result.ToHttpResult();
    }

    // ── Multi-Collection ─────────────────────────────────────────────────────

    private static async Task<IResult> GetAssetCollections(
        Guid id, [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.GetAssetCollectionsAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AddAssetToCollection(
        Guid id, Guid collectionId,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.AddToCollectionAsync(id, collectionId, ct);
        return result.ToHttpResult(v => Results.Created($"/api/assets/{id}/collections/{collectionId}", v));
    }

    private static async Task<IResult> RemoveAssetFromCollection(
        Guid id, Guid collectionId,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.RemoveFromCollectionAsync(id, collectionId, ct);
        return result.ToHttpResult();
    }

    // ── Renditions ───────────────────────────────────────────────────────────

    private static Func<Guid, IAssetService, CancellationToken, Task<IResult>> GetRendition(string size) =>
        async (Guid id, [FromServices] IAssetService svc, CancellationToken ct) =>
        {
            var result = await svc.GetRenditionUrlAsync(id, size, ct);
            return result.ToHttpResult(url => Results.Redirect(url));
        };
}
