using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AssetHub.Api.Endpoints;

public static class GuestInvitationEndpoints
{
    public static void MapGuestInvitationEndpoints(this WebApplication app)
    {
        // Admin-only group for invite / list / revoke.
        var adminGroup = app.MapGroup("/api/v1/admin/guest-invitations")
            .RequireAuthorization("RequireAdmin")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Guest Invitations");

        adminGroup.MapGet("/", List).WithName("ListGuestInvitations");
        adminGroup.MapPost("/", Create)
            .AddEndpointFilter<ValidationFilter<CreateGuestInvitationDto>>()
            .DisableAntiforgery()
            .WithName("CreateGuestInvitation");
        adminGroup.MapPost("{id:guid}/revoke", Revoke)
            .DisableAntiforgery()
            .WithName("RevokeGuestInvitation");

        // Anonymous accept — the magic-link target. Rate-limit shared
        // with anonymous share access since the threat profile is
        // identical (anonymous + signed-token).
        app.MapPost("/api/v1/guest-invitations/accept", Accept)
            .AllowAnonymous()
            .RequireRateLimiting(Application.Constants.RateLimitPolicies.ShareAnonymous)
            .DisableAntiforgery()
            .WithTags("Guest Invitations")
            .WithName("AcceptGuestInvitation");
    }

    private static async Task<IResult> List(
        [FromServices] IGuestInvitationService svc, CancellationToken ct)
        => (await svc.ListAsync(ct)).ToHttpResult();

    private static async Task<IResult> Create(
        CreateGuestInvitationDto dto,
        [FromServices] IGuestInvitationService svc,
        [FromServices] IOptions<AppSettings> appSettings,
        CancellationToken ct)
        => (await svc.CreateAsync(dto, appSettings.Value.BaseUrl, ct))
            .ToHttpResult(value => Results.Created(
                $"/api/v1/admin/guest-invitations/{value.Invitation.Id}", value));

    private static async Task<IResult> Revoke(
        Guid id, [FromServices] IGuestInvitationService svc, CancellationToken ct)
        => (await svc.RevokeAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> Accept(
        [FromBody] AcceptGuestInvitationRequest body,
        [FromServices] IGuestInvitationService svc,
        CancellationToken ct)
        => (await svc.AcceptAsync(body.Token, ct)).ToHttpResult();

    /// <summary>Body of <c>POST /api/v1/guest-invitations/accept</c>.</summary>
    public sealed record AcceptGuestInvitationRequest(string Token);
}
