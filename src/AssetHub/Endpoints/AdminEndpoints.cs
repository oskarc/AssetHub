using AssetHub.Extensions;
using Dam.Application.Dtos;
using Dam.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Endpoints;

/// <summary>
/// Admin-only endpoints for managing shares, collection access, and viewing users.
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
        group.MapPost("/shares/{id:guid}/revoke", RevokeShare).WithName("AdminRevokeShare");

        // ===== COLLECTION ACCESS MANAGEMENT =====
        group.MapGet("/collections/access", GetCollectionAccess).WithName("GetCollectionAccess");
        group.MapPost("/collections/{collectionId:guid}/acl", SetCollectionAccess).WithName("AdminSetCollectionAccess");
        group.MapDelete("/collections/{collectionId:guid}/acl/{principalId}", RemoveCollectionAccess).WithName("RemoveCollectionAccess");

        // ===== USER MANAGEMENT =====
        group.MapGet("/users", GetUsers).WithName("GetUsers");
        group.MapGet("/keycloak-users", GetKeycloakUsers).WithName("GetKeycloakUsers");
        group.MapPost("/users", CreateUser).WithName("CreateUser");
        group.MapPost("/users/{userId}/reset-password", ResetUserPassword).WithName("ResetUserPassword");
        group.MapPost("/users/sync", SyncDeletedUsers).WithName("SyncDeletedUsers");
        group.MapDelete("/users/{userId}", DeleteUser).WithName("DeleteUser");
    }

    // ── Share Management ─────────────────────────────────────────────────────

    private static async Task<IResult> GetAllShares(
        [FromServices] IAdminService svc, CancellationToken ct)
    {
        var result = await svc.GetAllSharesAsync(ct);
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
        HttpContext httpContext, CancellationToken ct)
    {
        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var result = await svc.CreateUserAsync(request, baseUrl, ct);
        return result.ToHttpResult(v => Results.Created($"/api/admin/users/{v.UserId}", v));
    }

    private static async Task<IResult> ResetUserPassword(
        [FromRoute] string userId, [FromBody] ResetPasswordRequest request,
        [FromServices] IAdminService svc, CancellationToken ct)
    {
        var result = await svc.ResetUserPasswordAsync(userId, request, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SyncDeletedUsers(
        [FromQuery] bool dryRun,
        [FromServices] IAdminService svc, CancellationToken ct)
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
}
