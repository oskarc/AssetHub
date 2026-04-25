using System.Security.Claims;
using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Api.OpenApi;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

/// <summary>
/// User-facing CRUD for the caller's own personal access tokens. Every authenticated
/// user can manage their own tokens; a token cannot be used to mint or revoke tokens
/// on its own account (guards against privilege escalation via a compromised PAT).
/// </summary>
public static class PersonalAccessTokenEndpoints
{
    public static void MapPersonalAccessTokenEndpoints(this WebApplication app)
    {
        // Note on PAT scope enforcement: this is the documented exception
        // to the "every PublicApi endpoint carries a RequireScopeFilter"
        // rule. The Create/Revoke handlers below check for a `pat_id` claim
        // and return 403 to any PAT-authenticated principal, which is
        // strictly stronger than a scope check (no scope, including admin,
        // is enough to bootstrap new credentials from a compromised token).
        var group = app.MapGroup("/api/v1/me/personal-access-tokens")
            .RequireAuthorization("RequireViewer")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("PersonalAccessTokens")
            .MarkAsPublicApi();

        group.MapGet("/", ListMine).WithName("ListMyPersonalAccessTokens");

        group.MapPost("/", Create)
            .AddEndpointFilter<ValidationFilter<CreatePersonalAccessTokenRequest>>()
            .DisableAntiforgery()
            .WithName("CreatePersonalAccessToken");

        group.MapDelete("/{id:guid}", Revoke)
            .DisableAntiforgery()
            .WithName("RevokePersonalAccessToken");
    }

    private static async Task<IResult> ListMine(
        [FromServices] IPersonalAccessTokenService svc,
        CancellationToken ct)
    {
        var result = await svc.ListMineAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Create(
        [FromBody] CreatePersonalAccessTokenRequest request,
        HttpContext http,
        [FromServices] IPersonalAccessTokenService svc,
        CancellationToken ct)
    {
        if (IsPatAuthenticated(http.User))
        {
            ServiceResult<CreatedPersonalAccessTokenDto> forbidden = ServiceError.Forbidden(
                "Personal access tokens cannot be created using a personal access token");
            return forbidden.ToHttpResult();
        }

        var result = await svc.CreateAsync(request, ct);
        return result.ToHttpResult(v =>
            Results.Created($"/api/v1/me/personal-access-tokens/{v.Token.Id}", v));
    }

    private static async Task<IResult> Revoke(
        Guid id,
        HttpContext http,
        [FromServices] IPersonalAccessTokenService svc,
        CancellationToken ct)
    {
        if (IsPatAuthenticated(http.User))
        {
            ServiceResult forbidden = ServiceError.Forbidden(
                "Personal access tokens cannot be revoked using a personal access token");
            return forbidden.ToHttpResult();
        }

        var result = await svc.RevokeAsync(id, ct);
        return result.ToHttpResult();
    }

    private static bool IsPatAuthenticated(ClaimsPrincipal user) =>
        user.FindFirst("pat_id") is not null;
}
