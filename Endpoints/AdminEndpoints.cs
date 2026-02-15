using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
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
        group.MapGet("/shares", GetAllShares)
            .WithName("GetAllShares")
            .WithSummary("Gets all shares with usage statistics (admin only)")
            .Produces<List<AdminShareDto>>();

        group.MapGet("/shares/{id:guid}/token", GetShareToken)
            .WithName("AdminGetShareToken")
            .WithSummary("Retrieves the plaintext token for a share if available (admin only)")
            .Produces<ShareTokenResponse>();

        group.MapPost("/shares/{id:guid}/revoke", RevokeShare)
            .WithName("AdminRevokeShare")
            .WithSummary("Revokes a share (admin only)");

        // ===== COLLECTION ACCESS MANAGEMENT =====
        group.MapGet("/collections/access", GetCollectionAccess)
            .WithName("GetCollectionAccess")
            .WithSummary("Gets all collections with ACLs in hierarchical structure (admin only)")
            .Produces<List<CollectionAccessDto>>();

        group.MapPost("/collections/{collectionId:guid}/acl", SetCollectionAccess)
            .WithName("AdminSetCollectionAccess")
            .WithSummary("Sets access for a user on a collection (admin only)");

        group.MapDelete("/collections/{collectionId:guid}/acl/{principalId}", RemoveCollectionAccess)
            .WithName("RemoveCollectionAccess")
            .WithSummary("Removes access for a user on a collection (admin only)");

        // ===== USER MANAGEMENT =====
        group.MapGet("/users", GetUsers)
            .WithName("GetUsers")
            .WithSummary("Gets all users with collection access (admin only)")
            .Produces<List<UserAccessSummaryDto>>();

        group.MapGet("/keycloak-users", GetKeycloakUsers)
            .WithName("GetKeycloakUsers")
            .WithSummary("Gets all users from Keycloak (admin only)")
            .Produces<List<KeycloakUserDto>>();

        group.MapPost("/users", CreateUser)
            .WithName("CreateUser")
            .WithSummary("Creates a new user in Keycloak (admin only)")
            .Produces<CreateUserResponse>(StatusCodes.Status201Created)
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status409Conflict);

        group.MapPost("/users/{userId}/reset-password", ResetUserPassword)
            .WithName("ResetUserPassword")
            .WithSummary("Resets a user's password in Keycloak (admin only)")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        group.MapPost("/users/sync", SyncDeletedUsers)
            .WithName("SyncDeletedUsers")
            .WithSummary("Scans for and cleans up users deleted from Keycloak (admin only)")
            .Produces<UserSyncResult>();

        group.MapDelete("/users/{userId}", DeleteUser)
            .WithName("DeleteUser")
            .WithSummary("Deletes a user from Keycloak and cleans up app data (admin only)")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status404NotFound);
    }

    // ===== SHARE MANAGEMENT =====

    private static async Task<IResult> GetAllShares(
        [FromServices] IShareRepository shareRepo,
        [FromServices] IUserLookupService userLookup,
        CancellationToken ct)
    {
        var shares = await shareRepo.GetAllAsync(includeAsset: true, includeCollection: true, ct);

        var userIds = shares.Select(s => s.CreatedByUserId).Distinct().ToList();
        var userNames = await userLookup.GetUserNamesAsync(userIds, ct);

        var result = shares.Select(s => new AdminShareDto
        {
            Id = s.Id,
            ScopeType = s.ScopeType,
            ScopeId = s.ScopeId,
            ScopeName = s.ScopeType == Constants.ScopeTypes.Asset
                ? s.Asset?.Title ?? "Unknown Asset"
                : s.Collection?.Name ?? "Unknown Collection",
            CreatedByUserId = s.CreatedByUserId,
            CreatedByUserName = userNames.TryGetValue(s.CreatedByUserId, out var name) ? name : $"Deleted User ({s.CreatedByUserId[..8]})",
            CreatedAt = s.CreatedAt,
            ExpiresAt = s.ExpiresAt,
            RevokedAt = s.RevokedAt,
            LastAccessedAt = s.LastAccessedAt,
            AccessCount = s.AccessCount,
            HasPassword = !string.IsNullOrEmpty(s.PasswordHash),
            Status = ShareHelpers.GetShareStatus(s.RevokedAt, s.ExpiresAt)
        }).ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> GetShareToken(
        Guid id,
        [FromServices] IShareRepository shareRepo,
        [FromServices] Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtection,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var share = await shareRepo.GetByIdAsync(id, ct);
        if (share == null)
            return Results.NotFound(ApiError.NotFound("Share not found"));

        if (string.IsNullOrEmpty(share.TokenEncrypted))
            return Results.NotFound(ApiError.NotFound("Share token not available — this share was created before token encryption was enabled"));

        try
        {
            var protector = dataProtection.CreateProtector(Constants.DataProtection.ShareTokenProtector);
            var protectedBytes = Convert.FromBase64String(share.TokenEncrypted);
            var token = System.Text.Encoding.UTF8.GetString(protector.Unprotect(protectedBytes));
            return Results.Ok(new ShareTokenResponse { Token = token });
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            var logger = loggerFactory.CreateLogger("AssetHub.AdminEndpoints");
            logger.LogError(ex, "Failed to decrypt share token for share {ShareId}. Data Protection keys may have been rotated.", id);
            return Results.Json(ApiError.ServerError("Unable to decrypt share token — encryption keys may have changed"), statusCode: 500);
        }
        catch (FormatException ex)
        {
            var logger = loggerFactory.CreateLogger("AssetHub.AdminEndpoints");
            logger.LogError(ex, "Corrupted TokenEncrypted data for share {ShareId}", id);
            return Results.Json(ApiError.ServerError("Share token data is corrupted"), statusCode: 500);
        }
    }

    private static async Task<IResult> RevokeShare(
        Guid id,
        [FromServices] IShareRepository shareRepo,
        [FromServices] IAuditService audit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var share = await shareRepo.GetByIdAsync(id, ct);
        if (share == null)
            return Results.NotFound(ApiError.NotFound("Share not found"));

        if (share.RevokedAt.HasValue)
            return Results.BadRequest(ApiError.BadRequest("Share is already revoked"));

        share.RevokedAt = DateTime.UtcNow;
        await shareRepo.UpdateAsync(share, ct);

        var adminUserId = httpContext.User.GetRequiredUserId();
        await audit.LogAsync("share.revoked", "share", id, adminUserId,
            new() { ["admin"] = true }, httpContext, ct);

        return Results.NoContent();
    }

    // ===== COLLECTION ACCESS MANAGEMENT =====

    private static async Task<IResult> GetCollectionAccess(
        [FromServices] ICollectionRepository collectionRepo,
        [FromServices] IUserLookupService userLookup,
        CancellationToken ct)
    {
        var collections = await collectionRepo.GetAllWithAclsAsync(ct);

        var allCollections = collections.ToList();
        var rootCollections = allCollections.Where(c => c.ParentId == null).ToList();

        var allUserIds = allCollections
            .SelectMany(c => c.Acls.Where(a => a.PrincipalType == "user").Select(a => a.PrincipalId))
            .Distinct()
            .ToList();
        var userNames = await userLookup.GetUserNamesAsync(allUserIds, ct);

        var result = rootCollections.Select(c => CollectionTreeHelper.BuildAccessTree(c, allCollections, userNames)).ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> SetCollectionAccess(
        Guid collectionId,
        [FromBody] SetCollectionAccessRequest request,
        [FromServices] ICollectionRepository collectionRepo,
        [FromServices] ICollectionAclRepository aclRepo,
        [FromServices] IUserLookupService userLookup,
        [FromServices] IAuditService audit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!await collectionRepo.ExistsAsync(collectionId, ct))
            return Results.NotFound(ApiError.NotFound("Collection not found"));

        if (string.IsNullOrWhiteSpace(request.PrincipalId))
            return Results.BadRequest(ApiError.BadRequest("PrincipalId is required"));

        var principalType = request.PrincipalType ?? "user";
        var principalId = request.PrincipalId;

        if (principalType == "user")
        {
            if (!Guid.TryParse(request.PrincipalId, out _))
            {
                var userId = await userLookup.GetUserIdByUsernameAsync(request.PrincipalId, ct);
                if (userId == null)
                    return Results.BadRequest(ApiError.BadRequest($"User '{request.PrincipalId}' not found"));
                principalId = userId;
            }
            else
            {
                var username = await userLookup.GetUserNameAsync(request.PrincipalId, ct);
                if (username == null)
                    return Results.BadRequest(ApiError.BadRequest($"User with ID '{request.PrincipalId}' not found"));
            }
        }

        var targetRole = request.Role!.ToLowerInvariant();
        if (!RoleHierarchy.AllRoles.Contains(targetRole))
            return Results.BadRequest(ApiError.BadRequest($"Invalid role '{targetRole}'"));

        var acl = await aclRepo.SetAccessAsync(collectionId, principalType, principalId, targetRole, ct);

        var adminUserId = httpContext.User.GetRequiredUserId();
        await audit.LogAsync("acl.set", "collection", collectionId, adminUserId,
            new() { ["principalType"] = principalType, ["principalId"] = principalId, ["role"] = targetRole, ["admin"] = true }, httpContext, ct);

        return Results.Ok(new AccessUpdatedResponse
        {
            Message = "Access updated",
            CollectionId = collectionId,
            PrincipalId = principalId,
            Role = acl.Role
        });
    }

    private static async Task<IResult> RemoveCollectionAccess(
        Guid collectionId,
        string principalId,
        [FromQuery] string? principalType,
        [FromServices] ICollectionRepository collectionRepo,
        [FromServices] ICollectionAclRepository aclRepo,
        [FromServices] IAuditService audit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!await collectionRepo.ExistsAsync(collectionId, ct))
            return Results.NotFound(ApiError.NotFound("Collection not found"));

        await aclRepo.RevokeAccessAsync(collectionId, principalType ?? "user", principalId, ct);

        var adminUserId = httpContext.User.GetRequiredUserId();
        await audit.LogAsync("acl.revoked", "collection", collectionId, adminUserId,
            new() { ["principalType"] = principalType ?? "user", ["principalId"] = principalId, ["admin"] = true }, httpContext, ct);

        return Results.Ok(new AccessRevokedResponse { Message = "Access revoked", CollectionId = collectionId, PrincipalId = principalId });
    }

    // ===== USER MANAGEMENT =====

    private static async Task<IResult> GetUsers(
        [FromServices] ICollectionAclRepository aclRepo,
        [FromServices] ICollectionRepository collectionRepo,
        [FromServices] IUserLookupService userLookup,
        CancellationToken ct)
    {
        var allAcls = await aclRepo.GetAllAsync(ct);
        var allCollections = (await collectionRepo.GetAllWithAclsAsync(ct)).ToDictionary(c => c.Id);

        var userIds = allAcls.Where(a => a.PrincipalType == "user").Select(a => a.PrincipalId).Distinct().ToList();
        var userNames = await userLookup.GetUserNamesAsync(userIds, ct);

        var userAccess = allAcls
            .Where(a => a.PrincipalType == "user")
            .GroupBy(a => a.PrincipalId)
            .Select(g => new UserAccessSummaryDto
            {
                UserId = g.Key,
                UserName = userNames.TryGetValue(g.Key, out var name) ? name : $"Deleted User ({g.Key[..Math.Min(8, g.Key.Length)]})",
                CollectionCount = g.Count(),
                HighestRole = RoleHierarchy.GetHighestRole(g.Select(a => a.Role)),
                Collections = g.Select(a => new UserCollectionAccessDto
                {
                    CollectionId = a.CollectionId,
                    CollectionName = allCollections.TryGetValue(a.CollectionId, out var col) ? col.Name : "Unknown",
                    Role = a.Role
                }).ToList()
            })
            .OrderBy(u => u.UserName)
            .ToList();

        return Results.Ok(userAccess);
    }

    private static async Task<IResult> GetKeycloakUsers(
        [FromServices] IUserLookupService userLookup,
        [FromServices] ICollectionAclRepository aclRepo,
        CancellationToken ct)
    {
        var allUsers = await userLookup.GetAllUsersAsync(ct);
        var allAcls = await aclRepo.GetAllAsync(ct);

        var userAclGroups = allAcls
            .Where(a => a.PrincipalType == "user")
            .GroupBy(a => a.PrincipalId)
            .ToDictionary(g => g.Key, g => new
            {
                CollectionCount = g.Count(),
                HighestRole = RoleHierarchy.GetHighestRole(g.Select(a => a.Role))
            });

        var result = allUsers.Select(u =>
        {
            var hasAccess = userAclGroups.TryGetValue(u.Id, out var acl);
            return new KeycloakUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                CreatedAt = u.CreatedAt,
                CollectionCount = hasAccess ? acl!.CollectionCount : 0,
                HighestRole = hasAccess ? acl!.HighestRole : null
            };
        }).ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateUser(
        [FromBody] CreateUserRequest request,
        [FromServices] IKeycloakUserService keycloakUserService,
        [FromServices] IUserProvisioningService provisioning,
        [FromServices] IAuditService audit,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        var firstName = request.FirstName?.Trim() ?? "";
        var lastName = request.LastName?.Trim() ?? "";

        if (!InputValidation.TryValidate(out var errors,
            ("username", InputValidation.ValidateUsername(username)),
            ("email", InputValidation.ValidateEmail(email)),
            ("firstName", InputValidation.ValidateRequired(firstName, "First name")),
            ("lastName", InputValidation.ValidateRequired(lastName, "Last name")),
            ("password", InputValidation.ValidatePassword(request.Password))))
        {
            return Results.BadRequest(ApiError.ValidationError("Validation failed", errors));
        }

        var collectionErrors = await provisioning.ValidateCollectionsExistAsync(request.InitialCollectionIds, ct);
        if (collectionErrors.Count > 0)
            return Results.BadRequest(ApiError.ValidationError("One or more collections not found", collectionErrors));

        try
        {
            var userId = await keycloakUserService.CreateUserAsync(
                username, email, firstName, lastName,
                request.Password, request.RequirePasswordChange, ct);

            logger.LogInformation("Admin created user '{Username}' (ID: {UserId})", username, userId);

            var role = RoleHierarchy.ResolveRole(request.InitialRole);
            await provisioning.GrantCollectionAccessAsync(request.InitialCollectionIds, userId, role, username, ct);

            if (request.SendWelcomeEmail && !string.IsNullOrWhiteSpace(email))
            {
                var baseUrl = configuration["App:BaseUrl"];
                if (string.IsNullOrWhiteSpace(baseUrl))
                    baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
                var adminUsername = httpContext.User.Identity?.Name ?? "An administrator";
                await provisioning.SendWelcomeEmailAsync(
                    email, username, request.Password, request.RequirePasswordChange,
                    baseUrl, adminUsername, ct);
            }

            var adminUserId = httpContext.User.GetRequiredUserId();
            await audit.LogAsync("user.created", "user", Guid.TryParse(userId, out var uid) ? uid : null, adminUserId,
                new() { ["username"] = username, ["email"] = email }, httpContext, ct);

            return Results.Created($"/api/admin/users/{userId}", new CreateUserResponse
            {
                UserId = userId,
                Username = username,
                Email = email,
                Message = request.InitialCollectionIds.Count > 0
                    ? $"User created and granted {role} access to {request.InitialCollectionIds.Count} collection(s)"
                    : "User created successfully"
            });
        }
        catch (KeycloakApiException ex)
        {
            logger.LogWarning(ex, "Keycloak API error creating user '{Username}'", username);

            var adminUserId = httpContext.User.GetRequiredUserId();
            await audit.LogAsync("user.create_failed", "user", null, adminUserId,
                new() { ["username"] = username, ["error"] = ex.Message }, httpContext, ct);
            return ex.StatusCode == 409
                ? Results.Conflict(ApiError.BadRequest(ex.Message))
                : Results.BadRequest(ApiError.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error creating user '{Username}'", username);
            return Results.Json(ApiError.ServerError("An unexpected error occurred"), statusCode: 500);
        }
    }

    private static async Task<IResult> ResetUserPassword(
        [FromRoute] string userId,
        [FromBody] ResetPasswordRequest request,
        [FromServices] IKeycloakUserService keycloakUserService,
        [FromServices] IAuditService audit,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Results.BadRequest(ApiError.BadRequest("User ID is required"));

        var passwordError = InputValidation.ValidatePassword(request.NewPassword);
        if (passwordError != null)
            return Results.BadRequest(ApiError.ValidationError("Validation failed",
                new Dictionary<string, string> { ["password"] = passwordError }));

        try
        {
            await keycloakUserService.ResetPasswordAsync(userId, request.NewPassword, request.Temporary, ct);

            logger.LogInformation("Admin reset password for user '{UserId}'", userId);

            var adminUserId = httpContext.User.GetRequiredUserId();
            await audit.LogAsync("user.password_reset", "user",
                Guid.TryParse(userId, out var uid) ? uid : null, adminUserId,
                new() { ["targetUserId"] = userId, ["temporary"] = request.Temporary.ToString() },
                httpContext, ct);

            return Results.Ok(new { Message = "Password reset successfully" });
        }
        catch (KeycloakApiException ex)
        {
            logger.LogWarning(ex, "Keycloak API error resetting password for user '{UserId}'", userId);
            return ex.StatusCode == 404
                ? Results.NotFound(ApiError.BadRequest(ex.Message))
                : Results.BadRequest(ApiError.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error resetting password for user '{UserId}'", userId);
            return Results.Json(ApiError.ServerError("An unexpected error occurred"), statusCode: 500);
        }
    }

    private static async Task<IResult> SyncDeletedUsers(
        [FromQuery] bool dryRun,
        [FromServices] IUserSyncService syncService,
        [FromServices] IAuditService audit,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        try
        {
            var result = await syncService.SyncDeletedUsersAsync(dryRun, ct);

            if (!dryRun && result.DeletedUsers > 0)
            {
                var adminUserId = httpContext.User.GetRequiredUserId();
                await audit.LogAsync("user.sync.completed", "system", null, adminUserId,
                    new()
                    {
                        ["deletedUsers"] = result.DeletedUsers,
                        ["aclsRemoved"] = result.AclsRemoved,
                        ["sharesRevoked"] = result.SharesRevoked
                    }, httpContext, ct);
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during user sync");
            return Results.Json(ApiError.ServerError("An unexpected error occurred"), statusCode: 500);
        }
    }

    private static async Task<IResult> DeleteUser(
        [FromRoute] string userId,
        [FromServices] IKeycloakUserService keycloakUserService,
        [FromServices] IUserCleanupService cleanupService,
        [FromServices] IUserLookupService userLookup,
        [FromServices] IAuditService audit,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Results.BadRequest(ApiError.BadRequest("User ID is required"));

        var adminUserId = httpContext.User.GetRequiredUserId();
        if (string.Equals(userId, adminUserId, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(ApiError.BadRequest("You cannot delete your own account"));

        var username = await userLookup.GetUserNameAsync(userId, ct);

        try
        {
            if (username != null)
            {
                await keycloakUserService.DeleteUserAsync(userId, ct);
            }
            else
            {
                logger.LogInformation("User '{UserId}' already absent from Keycloak, cleaning up app data only", userId);
                username = userId;
            }

            var (aclsRemoved, sharesRevoked) = await cleanupService.CleanupUserDataAsync(userId, ct);

            logger.LogInformation("Admin deleted user '{Username}' ({UserId}): removed {Acls} ACLs, revoked {Shares} shares",
                username, userId, aclsRemoved, sharesRevoked);

            await audit.LogAsync("user.deleted", "user",
                Guid.TryParse(userId, out var uid) ? uid : null, adminUserId,
                new()
                {
                    ["username"] = username,
                    ["aclsRemoved"] = aclsRemoved,
                    ["sharesRevoked"] = sharesRevoked
                }, httpContext, ct);

            return Results.Ok(new DeleteUserResponse
            {
                Message = $"User '{username}' deleted successfully",
                AclsRemoved = aclsRemoved,
                SharesRevoked = sharesRevoked
            });
        }
        catch (KeycloakApiException ex)
        {
            logger.LogWarning(ex, "Keycloak API error deleting user '{UserId}'", userId);
            return ex.StatusCode == 404
                ? Results.NotFound(ApiError.NotFound(ex.Message))
                : Results.BadRequest(ApiError.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error deleting user '{UserId}'", userId);
            return Results.Json(ApiError.ServerError("An unexpected error occurred"), statusCode: 500);
        }
    }
}
