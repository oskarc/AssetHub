using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets")
            .RequireAuthorization()
            .WithTags("Assets");

        group.MapGet("", GetAssets).RequireAuthorization("RequireAdmin").WithName("GetAssets");
        group.MapGet("all", GetAllAssets).RequireAuthorization("RequireAdmin").WithName("GetAllAssets");
        group.MapGet("{id:guid}", GetAsset).WithName("GetAsset");
        // .DisableAntiforgery() is required because antiforgery tokens cannot be
        // provided by either caller: the Blazor Server HttpClient (cookie-auth,
        // same-origin only) or external JWT Bearer clients (inherently CSRF-immune
        // since the token must be explicitly attached to each request).
        group.MapPost("", UploadAsset).DisableAntiforgery().WithName("UploadAsset");
        group.MapPatch("{id:guid}", UpdateAsset).AddEndpointFilter<ValidationFilter<UpdateAssetDto>>().DisableAntiforgery().WithName("UpdateAsset");
        group.MapDelete("{id:guid}", DeleteAsset).DisableAntiforgery().WithName("DeleteAsset");
        group.MapPost("bulk-delete", BulkDeleteAssets).AddEndpointFilter<ValidationFilter<BulkDeleteAssetsRequest>>().DisableAntiforgery().WithName("BulkDeleteAssets");
        group.MapGet("collection/{collectionId:guid}", GetAssetsByCollection).WithName("GetAssetsByCollection");

        group.MapGet("{id:guid}/collections", GetAssetCollections).WithName("GetAssetCollections");
        group.MapPost("{id:guid}/collections/{collectionId:guid}", AddAssetToCollection).DisableAntiforgery().WithName("AddAssetToCollection");
        group.MapDelete("{id:guid}/collections/{collectionId:guid}", RemoveAssetFromCollection).DisableAntiforgery().WithName("RemoveAssetFromCollection");
        group.MapGet("{id:guid}/deletion-context", GetAssetDeletionContext).WithName("GetAssetDeletionContext");

        group.MapPost("init-upload", InitUpload).AddEndpointFilter<ValidationFilter<InitUploadRequest>>().DisableAntiforgery().WithName("InitUpload");
        group.MapPost("{id:guid}/confirm-upload", ConfirmUpload).DisableAntiforgery().WithName("ConfirmUpload");

        group.MapPost("{id:guid}/save-copy", SaveImageCopy).AddEndpointFilter<ValidationFilter<SaveImageCopyRequest>>().DisableAntiforgery().WithName("SaveImageCopy");
        group.MapPost("{id:guid}/replace-file", ReplaceImageFile).AddEndpointFilter<ValidationFilter<ReplaceImageFileRequest>>().DisableAntiforgery().WithName("ReplaceImageFile");

        group.MapGet("{id:guid}/download", GetRendition("original", forceDownload: true)).WithName("DownloadOriginal");
        group.MapGet("{id:guid}/preview", GetRendition("original", forceDownload: false)).WithName("PreviewOriginal");
        group.MapGet("{id:guid}/thumb", GetRendition("thumb", forceDownload: false)).WithName("GetThumbnail");
        group.MapGet("{id:guid}/thumb/download", GetRendition("thumb", forceDownload: true)).WithName("DownloadThumbnail");
        group.MapGet("{id:guid}/medium", GetRendition("medium", forceDownload: false)).WithName("GetMedium");
        group.MapGet("{id:guid}/medium/download", GetRendition("medium", forceDownload: true)).WithName("DownloadMedium");
        group.MapGet("{id:guid}/poster", GetRendition("poster", forceDownload: false)).WithName("GetPoster");
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    private static async Task<IResult> GetAssets(
        [FromServices] IAssetQueryService svc, CancellationToken ct,
        int skip = 0, int take = 50)
    {
        take = Math.Clamp(take, 1, Constants.Limits.MaxPageSize);
        var result = await svc.GetAssetsByStatusAsync(AssetStatus.Ready.ToDbString(), skip, take, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAllAssets(
        [AsParameters] AllAssetsQuery q,
        [FromServices] IAssetQueryService svc, CancellationToken ct)
    {
        var take = Math.Clamp(q.Take, 1, Constants.Limits.MaxPageSize);
        var result = await svc.GetAllAssetsAsync(q.Query, q.Type, q.CollectionId, q.SortBy, q.Skip, take, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAsset(
        Guid id, [FromServices] IAssetQueryService svc, CancellationToken ct)
    {
        var result = await svc.GetAssetAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAssetsByCollection(
        [AsParameters] CollectionAssetsQuery q,
        [FromServices] IAssetQueryService svc, CancellationToken ct)
    {
        var take = Math.Clamp(q.Take, 1, Constants.Limits.MaxPageSize);
        var result = await svc.GetAssetsByCollectionAsync(q.CollectionId, q.Query, q.Type, q.SortBy, q.Skip, take, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAssetDeletionContext(
        Guid id, [FromServices] IAssetQueryService svc, CancellationToken ct)
    {
        var result = await svc.GetDeletionContextAsync(id, ct);
        return result.ToHttpResult();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    private static async Task<IResult> UploadAsset(
        IFormFile file, [FromForm] Guid collectionId, [FromForm] string title,
        [FromServices] IAssetUploadService svc, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { error = "File is required" });

        if (collectionId == Guid.Empty)
            return Results.BadRequest(new { error = "collectionId is required" });

        var titleError = InputValidation.ValidateAssetTitle(title);
        if (titleError != null)
            return Results.BadRequest(new { error = titleError });

        using var stream = file.OpenReadStream();
        var result = await svc.UploadAsync(stream, file.FileName, file.ContentType, file.Length, collectionId, title, ct);
        return result.ToHttpResult(v => Results.Accepted($"/api/v1/assets/{v.Id}", v));
    }

    private static async Task<IResult> UpdateAsset(
        Guid id, UpdateAssetDto dto,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.UpdateAsync(id, dto, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteAsset(
        Guid id, [FromQuery] Guid? fromCollectionId,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.DeleteAsync(id, fromCollectionId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> BulkDeleteAssets(
        [FromBody] BulkDeleteAssetsRequest request,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.BulkDeleteAsync(request, ct);
        return result.ToHttpResult();
    }

    // ── Presigned Upload ─────────────────────────────────────────────────────

    private static async Task<IResult> InitUpload(
        InitUploadRequest request,
        [FromServices] IAssetUploadService svc, CancellationToken ct)
    {
        var result = await svc.InitUploadAsync(request, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ConfirmUpload(
        Guid id, [FromServices] IAssetUploadService svc, CancellationToken ct)
    {
        var result = await svc.ConfirmUploadAsync(id, ct);
        return result.ToHttpResult();
    }

    // ── Image Editing ────────────────────────────────────────────────────────

    private static async Task<IResult> SaveImageCopy(
        Guid id, SaveImageCopyRequest request,
        [FromServices] IAssetUploadService svc, CancellationToken ct)
    {
        var result = await svc.SaveImageCopyAsync(id, request, ct);
        return result.ToHttpResult(value => Results.Created($"/api/v1/assets/{value.AssetId}", value));
    }

    private static async Task<IResult> ReplaceImageFile(
        Guid id, ReplaceImageFileRequest request,
        [FromServices] IAssetUploadService svc, CancellationToken ct)
    {
        var result = await svc.ReplaceImageFileAsync(id, request, ct);
        return result.ToHttpResult();
    }

    // ── Multi-Collection ─────────────────────────────────────────────────────

    private static async Task<IResult> GetAssetCollections(
        Guid id, [FromServices] IAssetQueryService svc, CancellationToken ct)
    {
        var result = await svc.GetAssetCollectionsAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AddAssetToCollection(
        Guid id, Guid collectionId,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.AddToCollectionAsync(id, collectionId, ct);
        return result.ToHttpResult(v => Results.Created($"/api/v1/assets/{id}/collections/{collectionId}", v));
    }

    private static async Task<IResult> RemoveAssetFromCollection(
        Guid id, Guid collectionId,
        [FromServices] IAssetService svc, CancellationToken ct)
    {
        var result = await svc.RemoveFromCollectionAsync(id, collectionId, ct);
        return result.ToHttpResult();
    }

    // ── Renditions ───────────────────────────────────────────────────────────

    private static Func<Guid, IAssetQueryService, CancellationToken, Task<IResult>> GetRendition(string size, bool forceDownload = false) =>
        async (Guid id, [FromServices] IAssetQueryService svc, CancellationToken ct) =>
        {
            var result = await svc.GetRenditionUrlAsync(id, size, forceDownload, ct);
            return result.ToHttpResult(url => Results.Redirect(url));
        };
}
