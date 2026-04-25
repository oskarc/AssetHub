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
        group.MapGet("{id:guid}", GetAsset).AddEndpointFilter(read).WithName("GetAsset").MarkAsPublicApi();
        // .DisableAntiforgery() is required because antiforgery tokens cannot be
        // provided by either caller: the Blazor Server HttpClient (cookie-auth,
        // same-origin only) or external JWT Bearer clients (inherently CSRF-immune
        // since the token must be explicitly attached to each request).
        group.MapPost("", UploadAsset).AddEndpointFilter(write).DisableAntiforgery().WithName("UploadAsset").MarkAsPublicApi();
        group.MapPatch("{id:guid}", UpdateAsset).AddEndpointFilter<ValidationFilter<UpdateAssetDto>>().AddEndpointFilter(write).DisableAntiforgery().WithName("UpdateAsset").MarkAsPublicApi();
        group.MapDelete("{id:guid}", DeleteAsset).AddEndpointFilter(write).DisableAntiforgery().WithName("DeleteAsset").MarkAsPublicApi();
        group.MapPost("bulk-delete", BulkDeleteAssets).AddEndpointFilter<ValidationFilter<BulkDeleteAssetsRequest>>().AddEndpointFilter(write).DisableAntiforgery().WithName("BulkDeleteAssets").MarkAsPublicApi();
        group.MapGet("collection/{collectionId:guid}", GetAssetsByCollection).AddEndpointFilter(read).WithName("GetAssetsByCollection").MarkAsPublicApi();

        group.MapGet("{id:guid}/collections", GetAssetCollections).AddEndpointFilter(read).WithName("GetAssetCollections").MarkAsPublicApi();
        group.MapPost("{id:guid}/collections/{collectionId:guid}", AddAssetToCollection).AddEndpointFilter(write).DisableAntiforgery().WithName("AddAssetToCollection").MarkAsPublicApi();
        group.MapDelete("{id:guid}/collections/{collectionId:guid}", RemoveAssetFromCollection).AddEndpointFilter(write).DisableAntiforgery().WithName("RemoveAssetFromCollection").MarkAsPublicApi();
        // deletion-context is a UI-oriented helper (pre-delete impact preview) — kept internal.
        group.MapGet("{id:guid}/deletion-context", GetAssetDeletionContext).WithName("GetAssetDeletionContext");
        group.MapGet("{id:guid}/derivatives", GetDerivatives).AddEndpointFilter(read).WithName("GetDerivatives").MarkAsPublicApi();

        group.MapPost("init-upload", InitUpload).AddEndpointFilter<ValidationFilter<InitUploadRequest>>().AddEndpointFilter(write).DisableAntiforgery().WithName("InitUpload").MarkAsPublicApi();
        group.MapPost("{id:guid}/confirm-upload", ConfirmUpload).AddEndpointFilter(write).DisableAntiforgery().WithName("ConfirmUpload").MarkAsPublicApi();

        // Image-editor save paths — UI-specific, stay internal.
        group.MapPost("{id:guid}/save-copy", SaveImageCopy).AddEndpointFilter<ValidationFilter<SaveImageCopyRequest>>().DisableAntiforgery().WithName("SaveImageCopy");
        group.MapPost("{id:guid}/replace-file", ReplaceImageFile).AddEndpointFilter<ValidationFilter<ReplaceImageFileRequest>>().DisableAntiforgery().WithName("ReplaceImageFile");

        group.MapGet("{id:guid}/download", GetRendition("original", forceDownload: true)).AddEndpointFilter(read).WithName("DownloadOriginal").MarkAsPublicApi();
        group.MapGet("{id:guid}/preview", GetRendition("original", forceDownload: false)).AddEndpointFilter(read).WithName("PreviewOriginal").MarkAsPublicApi();
        group.MapGet("{id:guid}/thumb", GetRendition("thumb", forceDownload: false)).AddEndpointFilter(read).WithName("GetThumbnail").MarkAsPublicApi();
        group.MapGet("{id:guid}/thumb/download", GetRendition("thumb", forceDownload: true)).AddEndpointFilter(read).WithName("DownloadThumbnail").MarkAsPublicApi();
        group.MapGet("{id:guid}/medium", GetRendition("medium", forceDownload: false)).AddEndpointFilter(read).WithName("GetMedium").MarkAsPublicApi();
        group.MapGet("{id:guid}/medium/download", GetRendition("medium", forceDownload: true)).AddEndpointFilter(read).WithName("DownloadMedium").MarkAsPublicApi();
        group.MapGet("{id:guid}/poster", GetRendition("poster", forceDownload: false)).AddEndpointFilter(read).WithName("GetPoster").MarkAsPublicApi();
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

    private static Func<Guid, IAssetQueryService, CancellationToken, Task<IResult>> GetRendition(string size, bool forceDownload = false) =>
        async (Guid id, [FromServices] IAssetQueryService svc, CancellationToken ct) =>
        {
            var result = await svc.GetRenditionUrlAsync(id, size, forceDownload, ct);
            return result.ToHttpResult(url => Results.Redirect(url));
        };
}
