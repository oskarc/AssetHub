using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AssetHub.Api.Endpoints;

public static class ShareEndpoints
{
    public static void MapShareEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/shares")
            .WithTags("Shares")
            // Filter no-ops for anonymous requests (no cookie session) and
            // for Bearer principals; cookie-authed mutations downstream go
            // through full antiforgery validation.
            .RequireAntiforgeryUnlessBearer();

        // Public endpoints (no auth required, rate-limited).
        // POST endpoints use .DisableAntiforgery() because they are called by
        // non-browser clients and share link consumers that cannot provide
        // antiforgery tokens. GET endpoints are exempt from antiforgery by default.
        group.MapGet("{token}/download", DownloadSharedAsset).WithName("DownloadSharedAsset")
            .AllowAnonymous().RequireRateLimiting(Constants.RateLimitPolicies.ShareAnonymous);
        group.MapPost("{token}/download-all", DownloadAllSharedAssets).WithName("DownloadAllSharedAssets")
            .AllowAnonymous().RequireRateLimiting(Constants.RateLimitPolicies.ShareAnonymous).DisableAntiforgery();
        group.MapGet("{token}/preview", PreviewSharedAsset).WithName("PreviewSharedAsset")
            .AllowAnonymous().RequireRateLimiting(Constants.RateLimitPolicies.ShareAnonymous);

        // Protected endpoints. NOTE: RequireAuthorization() here applies to the
        // WHOLE group (it mutates the same builder — this is not a sub-group);
        // the public endpoints above stay anonymous only because each carries
        // .AllowAnonymous(), which overrides. Any endpoint added to this group
        // is therefore authenticated by default.
        group.RequireAuthorization();
    }

    // ── Public endpoints ─────────────────────────────────────────────────────



    private static async Task<IResult> DownloadSharedAsset(
        string token, Guid? assetId, string? accessToken,
        [FromServices] IPublicShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var effectiveCredential = GetSharePassword(httpContext) ?? accessToken;
        var result = await svc.GetDownloadUrlAsync(token, effectiveCredential, assetId, ct);
        return HandleShareResult(result, url => Results.Redirect(url));
    }

    private static async Task<IResult> DownloadAllSharedAssets(
        string token, [FromQuery] string? accessToken,
        [FromServices] IPublicShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var effectiveCredential = GetSharePassword(httpContext) ?? accessToken;
        var result = await svc.EnqueueDownloadAllAsync(token, effectiveCredential, ct);
        if (!result.IsSuccess)
            return HandleShareResult(result);
        return Results.Accepted(result.Value!.StatusUrl, result.Value);
    }

    private static async Task<IResult> PreviewSharedAsset(
        string token, [AsParameters] SharePreviewQuery q,
        [FromServices] IPublicShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var effectiveCredential = GetSharePassword(httpContext) ?? q.AccessToken;
        var result = await svc.GetPreviewUrlAsync(token, effectiveCredential, q.Size, q.AssetId, q.Download, ct);
        return HandleShareResult(result, url => Results.Redirect(url));
    }

    // ── Protected endpoints ──────────────────────────────────────────────────




    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts share password from X-Share-Password header.
    /// Passwords are only accepted via header to avoid leakage in logs,
    /// browser history, and referrer headers. For HTML element attributes
    /// (img src, video src, a href) use short-lived access tokens instead.
    /// </summary>
    private static string? GetSharePassword(HttpContext httpContext)
    {
        return httpContext.Request.Headers["X-Share-Password"].FirstOrDefault();
    }

    /// <summary>
    /// Handles ServiceResult for share endpoints, with special handling for
    /// PASSWORD_REQUIRED (401) responses that need a specific body format.
    /// </summary>
    private static IResult HandleShareResult<T>(ServiceResult<T> result, Func<T, IResult>? onSuccess = null)
    {
        if (result.IsSuccess)
            return onSuccess is not null ? onSuccess(result.Value!) : Results.Ok(result.Value);

        return result.Error!.Code switch
        {
            "PASSWORD_REQUIRED" => Results.Json(
                new PasswordRequiredResponse { RequiresPassword = true }, statusCode: 401),
            "UNAUTHORIZED" => Results.Unauthorized(),
            _ => result.ToHttpResult()
        };
    }

}
