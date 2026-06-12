using AssetHub.Api.Authentication;
using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Api.OpenApi;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "Endpoint mapping class — wires up the asset CRUD / upload / search / version / metadata endpoints. Coupling is the point.")]
public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets")
            .RequireAuthorization()
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Assets");

        // PAT scope enforcement: `.AddEndpointFilter(new RequireScopeFilter(...))` only
        // rejects PAT principals that lack the required scope. Cookie / JWT principals
        // and PATs with no declared scopes (full owner impersonation) pass unchanged.
        var read = new RequireScopeFilter("assets:read");
        var write = new RequireScopeFilter("assets:write");

        // Admin-only asset listing — intentionally NOT part of the public OpenAPI contract.
        group.MapGet("", GetAssets).RequireAuthorization("RequireAdmin").WithName("GetAssets");
        // GET /all retired — POST /search (AssetSearchEndpoints) is the single asset-listing path.
        // MarkAsPublicRead / MarkAsPublicMutation bundle scope + antiforgery + OpenAPI
        // inclusion (see PublicApiEndpointExtensions). Mutations rely on the group-level
        // RequireAntiforgeryUnlessBearer() above as the actual CSRF gate for cookie
        // principals; Bearer clients are inherently CSRF-immune.
        group.MapGet("{id:guid}", GetAsset).MarkAsPublicRead(read).WithName("GetAsset");
        group.MapPost("", UploadAsset).MarkAsPublicMutation(write).WithName("UploadAsset");
        group.MapPatch("{id:guid}", UpdateAsset).AddEndpointFilter<ValidationFilter<UpdateAssetDto>>().MarkAsPublicMutation(write).WithName("UpdateAsset");
        group.MapDelete("{id:guid}", DeleteAsset).MarkAsPublicMutation(write).WithName("DeleteAsset");
        group.MapPost("bulk-delete", BulkDeleteAssets).AddEndpointFilter<ValidationFilter<BulkDeleteAssetsRequest>>().MarkAsPublicMutation(write).WithName("BulkDeleteAssets");
        group.MapGet("collection/{collectionId:guid}", GetAssetsByCollection).MarkAsPublicRead(read).WithName("GetAssetsByCollection");

        group.MapGet("{id:guid}/collections", GetAssetCollections).MarkAsPublicRead(read).WithName("GetAssetCollections");
        group.MapPost("{id:guid}/collections/{collectionId:guid}", AddAssetToCollection).MarkAsPublicMutation(write).WithName("AddAssetToCollection");
        group.MapDelete("{id:guid}/collections/{collectionId:guid}", RemoveAssetFromCollection).MarkAsPublicMutation(write).WithName("RemoveAssetFromCollection");
        // deletion-context is a UI-oriented helper (pre-delete impact preview) — kept internal.
        group.MapGet("{id:guid}/deletion-context", GetAssetDeletionContext).WithName("GetAssetDeletionContext");
        group.MapGet("{id:guid}/derivatives", GetDerivatives).MarkAsPublicRead(read).WithName("GetDerivatives");

        group.MapPost("init-upload", InitUpload).AddEndpointFilter<ValidationFilter<InitUploadRequest>>().MarkAsPublicMutation(write).WithName("InitUpload");
        group.MapPost("{id:guid}/confirm-upload", ConfirmUpload).MarkAsPublicMutation(write).WithName("ConfirmUpload");

        // Image-editor save paths — UI-specific, stay internal.
        group.MapPost("{id:guid}/save-copy", SaveImageCopy).AddEndpointFilter<ValidationFilter<SaveImageCopyRequest>>().DisableAntiforgery().WithName("SaveImageCopy");
        group.MapPost("{id:guid}/replace-file", ReplaceImageFile).AddEndpointFilter<ValidationFilter<ReplaceImageFileRequest>>().DisableAntiforgery().WithName("ReplaceImageFile");

        group.MapGet("{id:guid}/download", GetRendition("original", forceDownload: true)).MarkAsPublicRead(read).WithName("DownloadOriginal");
        group.MapGet("{id:guid}/preview", GetRendition("original", forceDownload: false)).MarkAsPublicRead(read).WithName("PreviewOriginal");
        group.MapGet("{id:guid}/thumb", GetRendition("thumb", forceDownload: false)).MarkAsPublicRead(read).WithName("GetThumbnail");
        group.MapGet("{id:guid}/thumb/download", GetRendition("thumb", forceDownload: true)).MarkAsPublicRead(read).WithName("DownloadThumbnail");
        group.MapGet("{id:guid}/medium", GetRendition("medium", forceDownload: false)).MarkAsPublicRead(read).WithName("GetMedium");
        group.MapGet("{id:guid}/medium/download", GetRendition("medium", forceDownload: true)).MarkAsPublicRead(read).WithName("DownloadMedium");
        group.MapGet("{id:guid}/poster", GetRendition("poster", forceDownload: false)).MarkAsPublicRead(read).WithName("GetPoster");
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

    private static async Task<IResult> GetDerivatives(
        Guid id, [FromServices] IAssetQueryService svc, CancellationToken ct)
    {
        var result = await svc.GetDerivativesAsync(id, ct);
        return result.ToHttpResult();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    private static async Task<IResult> UploadAsset(
        IFormFile file, [FromForm] Guid collectionId, [FromForm] string title,
        [FromQuery] bool force,
        [FromServices] IAssetUploadService svc, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(ApiError.BadRequest("File is required"));

        if (collectionId == Guid.Empty)
            return Results.BadRequest(ApiError.BadRequest("collectionId is required"));

        var titleError = InputValidation.ValidateAssetTitle(title);
        if (titleError is not null)
            return Results.BadRequest(ApiError.BadRequest(titleError));

        using var stream = file.OpenReadStream();
        var result = await svc.UploadAsync(stream, file.FileName, file.ContentType, file.Length, collectionId, title, skipDuplicateCheck: force, ct: ct);
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
        Guid id, [FromServices] IAssetUploadService svc, CancellationToken ct,
        [FromQuery] bool force = false)
    {
        // `force` defaults to false so callers that don't want to override duplicate detection can
        // POST with no query string. Minimal APIs treat parameters without defaults as required
        // and return 400 before the handler runs — which is what broke CI after T1-DUP-01 landed.
        var result = await svc.ConfirmUploadAsync(id, skipDuplicateCheck: force, ct: ct);
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

    /// <summary>
    /// Resolves a rendition for download — either a 302 redirect (un-watermarked,
    /// CDN-fast) or a watermarked byte stream (T5-WMK-01). The dispatch happens in
    /// <see cref="IAssetQueryService.ResolveRenditionDownloadAsync"/>; the endpoint
    /// just unwraps the discriminated result.
    /// </summary>
    private static Func<Guid, IAssetQueryService, CancellationToken, Task<IResult>> GetRendition(string size, bool forceDownload = false) =>
        async (Guid id, [FromServices] IAssetQueryService svc, CancellationToken ct) =>
        {
            var result = await svc.ResolveRenditionDownloadAsync(id, size, forceDownload, shareId: null, ct);
            if (!result.IsSuccess)
                return result.ToHttpResult();

            var dl = result.Value!;
            if (dl.PresignedUrl is not null)
                return Results.Redirect(dl.PresignedUrl);

            var stream = dl.Stream!;
            return Results.Stream(
                stream.Content,
                stream.ContentType,
                fileDownloadName: stream.ForceDownload ? stream.DownloadFileName : null,
                enableRangeProcessing: false);
        };
}
