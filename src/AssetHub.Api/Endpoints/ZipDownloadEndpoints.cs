using AssetHub.Api.Extensions;
using AssetHub.Application;
using AssetHub.Application.Helpers;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

/// <summary>
/// Endpoints for queued ZIP downloads (status polling and presigned download URLs).
/// </summary>
public static class ZipDownloadEndpoints
{
    public static void MapZipDownloadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/zip-downloads")
            .WithTags("ZipDownloads");

        // Authenticated status check (collection downloads)
        group.MapGet("{jobId:guid}", GetZipStatus)
            .WithName("GetZipDownloadStatus")
            .RequireAuthorization();

        // Anonymous status check (share downloads) — requires share token in header
        group.MapGet("{jobId:guid}/share", GetShareZipStatus)
            .WithName("GetShareZipDownloadStatus")
            .AllowAnonymous()
            .RequireRateLimiting(Constants.RateLimitPolicies.ShareAnonymous);
    }

    private static async Task<IResult> GetZipStatus(
        Guid jobId,
        [FromServices] IZipBuildService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var userId = httpContext.User.GetUserId();
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var result = await svc.GetStatusAsync(jobId, userId, null, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetShareZipStatus(
        Guid jobId,
        [FromServices] IZipBuildService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var shareToken = httpContext.Request.Headers["X-Share-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(shareToken))
            return Results.Unauthorized();

        var tokenHash = ShareHelpers.ComputeTokenHash(shareToken);
        var result = await svc.GetStatusAsync(jobId, null, tokenHash, ct);
        return result.ToHttpResult();
    }
}
