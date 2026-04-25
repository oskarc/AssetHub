using AssetHub.Api.Extensions;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class RenditionEndpoints
{
    public static void MapRenditionEndpoints(this WebApplication app)
    {
        // RequireViewer + the service double-checks the asset's collection ACL.
        // No PublicApi mark — the contract is admin/integrator-driven and may
        // evolve as we wire signed-URL embedding (FOLLOW-UP).
        var group = app.MapGroup("/api/v1/assets/{id:guid}/render")
            .RequireAuthorization("RequireViewer")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Asset Renditions");

        group.MapGet("/", Render).WithName("RenderAsset");
    }

    private static async Task<IResult> Render(
        Guid id,
        [FromQuery] int? w,
        [FromQuery] int? h,
        [FromQuery] string? fit,
        [FromQuery] string? fmt,
        [FromServices] IRenditionService svc,
        CancellationToken ct)
    {
        var request = new RenditionRequest(
            Width: w,
            Height: h,
            FitMode: fit ?? "contain",
            Format: fmt ?? "jpeg");

        var result = await svc.GetOrGenerateAsync(id, request, ct);
        if (!result.IsSuccess) return result.ToHttpResult();

        // 302 redirect — the browser fetches the rendition straight from
        // MinIO via the presigned URL, no API bandwidth used. Cache-friendly:
        // the URL is stable for the rendition's life so a CDN in front of
        // the image bucket can cache it.
        return Results.Redirect(result.Value!.Url, permanent: false);
    }
}
