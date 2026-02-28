using AssetHub.Api.Extensions;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AssetHub.Api.Endpoints;

/// <summary>
/// Admin-only endpoints for managing shares, collection access, users, and audit logs.
/// All endpoints require the 'admin' role.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization("RequireAdmin")
            .WithTags("Admin");

        // ===== SHARE MANAGEMENT =====
        group.MapGet("/shares", GetAllShares).WithName("GetAllShares");
        group.MapGet("/shares/{id:guid}/token", GetShareToken).WithName("AdminGetShareToken");
        group.MapDelete("/shares/{id:guid}", RevokeShare).DisableAntiforgery().WithName("AdminRevokeShare");

        // ===== COLLECTION ACCESS MANAGEMENT =====
        group.MapGet("/collections/access", GetCollectionAccess).WithName("GetCollectionAccess");
        group.MapPost("/collections/{collectionId:guid}/acl", SetCollectionAccess).DisableAntiforgery().WithName("AdminSetCollectionAccess");
        group.MapDelete("/collections/{collectionId:guid}/acl/{principalId}", RemoveCollectionAccess).DisableAntiforgery().WithName("RemoveCollectionAccess");

        // ===== USER MANAGEMENT =====
        group.MapGet("/users", GetUsers).WithName("GetUsers");
        group.MapGet("/keycloak-users", GetKeycloakUsers).WithName("GetKeycloakUsers");
        group.MapPost("/users", CreateUser).DisableAntiforgery().WithName("CreateUser");
        group.MapPost("/users/{userId}/reset-password", ResetUserPassword).DisableAntiforgery().WithName("ResetUserPassword");
        group.MapPost("/users/sync", SyncDeletedUsers).DisableAntiforgery().WithName("SyncDeletedUsers");
        group.MapDelete("/users/{userId}", DeleteUser).DisableAntiforgery().WithName("DeleteUser");

        // ===== AUDIT LOG =====
        group.MapGet("/audit", GetAuditEvents).WithName("GetAuditEvents");
        group.MapGet("/audit/paginated", GetAuditEventsPaginated).WithName("GetAuditEventsPaginated");
    }

    // ── Share Management ─────────────────────────────────────────────────────

    private static async Task<IResult> GetAllShares(
        [FromServices] IAdminService svc, CancellationToken ct,
        [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, Constants.Limits.AdminShareQueryLimit);
        var result = await svc.GetAllSharesAsync(skip, take, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetShareToken(
        Guid id, [FromServices] IAdminService svc, CancellationToken ct)
    {
        var result = await svc.GetShareTokenAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevokeShare(
        Guid id, [FromServices] IAdminService svc, CancellationToken ct)
    {
        var result = await svc.AdminRevokeShareAsync(id, ct);
        return result.ToHttpResult();
    }

    // ── Collection Access Management ─────────────────────────────────────────

    private static async Task<IResult> GetCollectionAccess(
        [FromServices] ICollectionAclService svc, CancellationToken ct)
    {
        var result = await svc.GetCollectionAccessTreeAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SetCollectionAccess(
        Guid collectionId, [FromBody] SetCollectionAccessRequest request,
        [FromServices] ICollectionAclService svc, CancellationToken ct)
    {
        var result = await svc.AdminSetAccessAsync(collectionId, request, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RemoveCollectionAccess(
        Guid collectionId, string principalId, [FromQuery] string? principalType,
        [FromServices] ICollectionAclService svc, CancellationToken ct)
    {
        var result = await svc.AdminRevokeAccessAsync(collectionId, principalType ?? "user", principalId, ct);
        return result.ToHttpResult();
    }

    // ── User Management ──────────────────────────────────────────────────────

    private static async Task<IResult> GetUsers(
        [FromServices] IAdminService svc, CancellationToken ct)
    {
        var result = await svc.GetUsersAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetKeycloakUsers(
        [FromServices] IAdminService svc, CancellationToken ct)
    {
        var result = await svc.GetKeycloakUsersAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateUser(
        [FromBody] CreateUserRequest request,
        [FromServices] IAdminService svc,
        [FromServices] IOptions<AppSettings> appSettings,
        CancellationToken ct)
    {
        var baseUrl = (appSettings.Value.BaseUrl ?? "").TrimEnd('/');
        var result = await svc.CreateUserAsync(request, baseUrl, ct);
        return result.ToHttpResult(v => Results.Created($"/api/admin/users/{v.UserId}", v));
    }

    private static async Task<IResult> ResetUserPassword(
        [FromRoute] string userId,
        [FromServices] IAdminService svc, CancellationToken ct)
    {
        var result = await svc.SendPasswordResetEmailAsync(userId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SyncDeletedUsers(
        [FromServices] IAdminService svc,
        CancellationToken ct,
        [FromQuery] bool dryRun = true)
    {
        var result = await svc.SyncDeletedUsersAsync(dryRun, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteUser(
        [FromRoute] string userId,
        [FromServices] IAdminService svc, CancellationToken ct)
    {
        var result = await svc.DeleteUserAsync(userId, ct);
        return result.ToHttpResult();
    }

    // ── Audit Log ─────────────────────────────────────────────────────────────

    private static async Task<IResult> GetAuditEvents(
        [FromServices] IAuditQueryService svc,
        CancellationToken ct,
        [FromQuery] int take = 200)
    {
        take = Math.Clamp(take, 1, Constants.Limits.MaxPageSize);
        var result = await svc.GetRecentAuditEventsAsync(take, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAuditEventsPaginated(
        [FromServices] IAuditQueryService svc,
        CancellationToken ct,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTime? cursor = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? targetType = null,
        [FromQuery] string? actorUserId = null)
    {
        var request = new AuditQueryRequest
        {
            PageSize = pageSize,
            Cursor = cursor,
            EventType = eventType,
            TargetType = targetType,
            ActorUserId = actorUserId
        };
        var result = await svc.GetAuditEventsAsync(request, ct);
        return result.ToHttpResult();
    }
}
