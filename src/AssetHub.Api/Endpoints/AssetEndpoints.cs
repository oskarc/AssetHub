using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "Endpoint mapping class — wires up the asset media endpoints (renditions, preview, download). Coupling is the point.")]
public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets")
            .RequireAuthorization()
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Assets");

        // Admin-only asset listing.
        // GET /all retired — POST /search (AssetSearchEndpoints) is the single asset-listing path.
        // Mutations rely on the group-level RequireAntiforgeryUnlessBearer() above as the
        // CSRF gate for cookie principals; Bearer clients are inherently CSRF-immune.

        // deletion-context is a UI-oriented helper (pre-delete impact preview) — kept internal.



        group.MapGet("{id:guid}/download", GetRendition("original", forceDownload: true)).WithName("DownloadOriginal");
        group.MapGet("{id:guid}/preview", GetRendition("original", forceDownload: false)).WithName("PreviewOriginal");
        group.MapGet("{id:guid}/thumb", GetRendition("thumb", forceDownload: false)).WithName("GetThumbnail");
        group.MapGet("{id:guid}/thumb/download", GetRendition("thumb", forceDownload: true)).WithName("DownloadThumbnail");
        group.MapGet("{id:guid}/medium", GetRendition("medium", forceDownload: false)).WithName("GetMedium");
        group.MapGet("{id:guid}/medium/download", GetRendition("medium", forceDownload: true)).WithName("DownloadMedium");
        group.MapGet("{id:guid}/poster", GetRendition("poster", forceDownload: false)).WithName("GetPoster");
    }

    // ── Queries ──────────────────────────────────────────────────────────────





    // ── Commands ─────────────────────────────────────────────────────────────





    // ── Presigned Upload ─────────────────────────────────────────────────────



    // ── Image Editing ────────────────────────────────────────────────────────



    // ── Multi-Collection ─────────────────────────────────────────────────────




    // ── Renditions ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a rendition for download — a 302 redirect to a presigned URL
    /// (CDN-fast), resolved in <see cref="IAssetQueryService.ResolveRenditionDownloadAsync"/>.
    /// </summary>
    private static Func<Guid, IAssetQueryService, CancellationToken, Task<IResult>> GetRendition(string size, bool forceDownload = false) =>
        async (Guid id, [FromServices] IAssetQueryService svc, CancellationToken ct) =>
        {
            var result = await svc.ResolveRenditionDownloadAsync(id, size, forceDownload, shareId: null, ct);
            if (!result.IsSuccess)
                return result.ToHttpResult();

            return Results.Redirect(result.Value!);
        };
}
