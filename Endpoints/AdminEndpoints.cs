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
        
        /// <summary>
        /// Gets all shares in the system with usage statistics.
        /// </summary>
        group.MapGet("/shares", async (
            [FromServices] IShareRepository shareRepo,
            [FromServices] IUserLookupService userLookup,
            CancellationToken ct) =>
        {
            var shares = await shareRepo.GetAllAsync(includeAsset: true, includeCollection: true, ct);
            
            // Lookup usernames for all creators
            var userIds = shares.Select(s => s.CreatedByUserId).Distinct().ToList();
            var userNames = await userLookup.GetUserNamesAsync(userIds, ct);
            
            var result = shares.Select(s => new AdminShareDto
            {
                Id = s.Id,
                ScopeType = s.ScopeType,
                ScopeId = s.ScopeId,
                ScopeName = s.ScopeType == "asset" 
                    ? s.Asset?.Title ?? "Unknown Asset"
                    : s.Collection?.Name ?? "Unknown Collection",
                CreatedByUserId = s.CreatedByUserId,
                CreatedByUserName = userNames.TryGetValue(s.CreatedByUserId, out var name) ? name : s.CreatedByUserId,
                CreatedAt = s.CreatedAt,
                ExpiresAt = s.ExpiresAt,
                RevokedAt = s.RevokedAt,
                LastAccessedAt = s.LastAccessedAt,
                AccessCount = s.AccessCount,
                HasPassword = !string.IsNullOrEmpty(s.PasswordHash),
                Status = ShareHelpers.GetShareStatus(s.RevokedAt, s.ExpiresAt)
            }).ToList();
            
            return Results.Ok(result);
        })
        .WithName("GetAllShares")
        .WithSummary("Gets all shares with usage statistics (admin only)")
        .Produces<List<AdminShareDto>>();

        /// <summary>
        /// Revokes a share by setting its RevokedAt timestamp.
        /// </summary>
        group.MapPost("/shares/{id:guid}/revoke", async (
            Guid id,
            [FromServices] IShareRepository shareRepo,
            CancellationToken ct) =>
        {
            var share = await shareRepo.GetByIdAsync(id, ct);
            if (share == null)
                return Results.NotFound(ApiError.NotFound("Share not found"));

            if (share.RevokedAt.HasValue)
                return Results.BadRequest(ApiError.BadRequest("Share is already revoked"));

            share.RevokedAt = DateTime.UtcNow;
            await shareRepo.UpdateAsync(share, ct);

            return Results.Ok(new { message = "Share revoked successfully", revokedAt = share.RevokedAt });
        })
        .WithName("AdminRevokeShare")
        .WithSummary("Revokes a share (admin only)");

        // ===== COLLECTION ACCESS MANAGEMENT =====
        
        /// <summary>
        /// Gets all collections with their ACL entries in a hierarchical structure.
        /// </summary>
        group.MapGet("/collections/access", async (
            [FromServices] ICollectionRepository collectionRepo,
            [FromServices] IUserLookupService userLookup,
            CancellationToken ct) =>
        {
            var collections = await collectionRepo.GetAllWithAclsAsync(ct);
            
            // Build hierarchical structure
            var allCollections = collections.ToList();
            var rootCollections = allCollections.Where(c => c.ParentId == null).ToList();
            
            // Lookup usernames for all principals
            var allUserIds = allCollections
                .SelectMany(c => c.Acls.Where(a => a.PrincipalType == "user").Select(a => a.PrincipalId))
                .Distinct()
                .ToList();
            var userNames = await userLookup.GetUserNamesAsync(allUserIds, ct);
            
            var result = rootCollections.Select(c => CollectionTreeHelper.BuildAccessTree(c, allCollections, userNames)).ToList();
            
            return Results.Ok(result);
        })
        .WithName("GetCollectionAccess")
        .WithSummary("Gets all collections with ACLs in hierarchical structure (admin only)")
        .Produces<List<CollectionAccessDto>>();

        /// <summary>
        /// Sets access for a user on a collection.
        /// The principalId can be either a username or a user ID.
        /// </summary>
        group.MapPost("/collections/{collectionId:guid}/acl", async (
            Guid collectionId,
            [FromBody] SetCollectionAccessRequest request,
            [FromServices] ICollectionRepository collectionRepo,
            [FromServices] ICollectionAclRepository aclRepo,
            [FromServices] IUserLookupService userLookup,
            CancellationToken ct) =>
        {
            if (!await collectionRepo.ExistsAsync(collectionId, ct))
                return Results.NotFound(ApiError.NotFound("Collection not found"));

            if (string.IsNullOrWhiteSpace(request.PrincipalId))
                return Results.BadRequest(ApiError.BadRequest("PrincipalId is required"));

            if (!RoleHierarchy.AllRoles.Contains(request.Role?.ToLowerInvariant() ?? ""))
                return Results.BadRequest(ApiError.BadRequest($"Invalid role. Must be one of: {string.Join(", ", RoleHierarchy.AllRoles)}"));

            // For user principals, resolve username to user ID and validate user exists
            var principalType = request.PrincipalType ?? "user";
            var principalId = request.PrincipalId;
            
            if (principalType == "user")
            {
                // Check if the input looks like a GUID (user ID) or a username
                if (!Guid.TryParse(request.PrincipalId, out _))
                {
                    // It's a username, look up the user ID
                    var userId = await userLookup.GetUserIdByUsernameAsync(request.PrincipalId, ct);
                    if (userId == null)
                        return Results.BadRequest(ApiError.BadRequest($"User '{request.PrincipalId}' not found"));
                    principalId = userId;
                }
                else
                {
                    // It's a user ID, verify it exists
                    var username = await userLookup.GetUserNameAsync(request.PrincipalId, ct);
                    if (username == null)
                        return Results.BadRequest(ApiError.BadRequest($"User with ID '{request.PrincipalId}' not found"));
                }
            }

            var acl = await aclRepo.SetAccessAsync(
                collectionId, 
                principalType, 
                principalId, 
                request.Role!.ToLowerInvariant(),
                ct);

            return Results.Ok(new { 
                message = "Access updated", 
                collectionId, 
                principalId,
                role = acl.Role 
            });
        })
        .WithName("AdminSetCollectionAccess")
        .WithSummary("Sets access for a user on a collection (admin only)");

        /// <summary>
        /// Removes access for a user on a collection.
        /// </summary>
        group.MapDelete("/collections/{collectionId:guid}/acl/{principalId}", async (
            Guid collectionId,
            string principalId,
            [FromQuery] string? principalType,
            [FromServices] ICollectionRepository collectionRepo,
            [FromServices] ICollectionAclRepository aclRepo,
            CancellationToken ct) =>
        {
            if (!await collectionRepo.ExistsAsync(collectionId, ct))
                return Results.NotFound(ApiError.NotFound("Collection not found"));

            await aclRepo.RevokeAccessAsync(collectionId, principalType ?? "user", principalId, ct);

            return Results.Ok(new { message = "Access revoked", collectionId, principalId });
        })
        .WithName("RemoveCollectionAccess")
        .WithSummary("Removes access for a user on a collection (admin only)");

        // ===== USER LISTING (from ACLs) =====
        
        /// <summary>
        /// Gets all users who have access to any collection (derived from ACLs).
        /// </summary>
        group.MapGet("/users", async (
            [FromServices] ICollectionAclRepository aclRepo,
            [FromServices] ICollectionRepository collectionRepo,
            [FromServices] IUserLookupService userLookup,
            CancellationToken ct) =>
        {
            var allAcls = await aclRepo.GetAllAsync(ct);
            var allCollections = (await collectionRepo.GetAllWithAclsAsync(ct)).ToDictionary(c => c.Id);
            
            // Lookup usernames
            var userIds = allAcls.Where(a => a.PrincipalType == "user").Select(a => a.PrincipalId).Distinct().ToList();
            var userNames = await userLookup.GetUserNamesAsync(userIds, ct);
            
            // Group by user
            var userAccess = allAcls
                .Where(a => a.PrincipalType == "user")
                .GroupBy(a => a.PrincipalId)
                .Select(g => new UserAccessSummaryDto
                {
                    UserId = g.Key,
                    UserName = userNames.TryGetValue(g.Key, out var name) ? name : g.Key,
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
        })
        .WithName("GetUsers")
        .WithSummary("Gets all users with collection access (admin only)")
        .Produces<List<UserAccessSummaryDto>>();

        /// <summary>
        /// Gets all users from Keycloak realm (admin only).
        /// </summary>
        group.MapGet("/keycloak-users", async (
            [FromServices] IUserLookupService userLookup,
            [FromServices] ICollectionAclRepository aclRepo,
            CancellationToken ct) =>
        {
            var allUsers = await userLookup.GetAllUsersAsync(ct);
            var allAcls = await aclRepo.GetAllAsync(ct);
            
            // Group ACLs by user to get collection count and highest role
            var userAclGroups = allAcls
                .Where(a => a.PrincipalType == "user")
                .GroupBy(a => a.PrincipalId)
                .ToDictionary(g => g.Key, g => new
                {
                    CollectionCount = g.Count(),
                    HighestRole = RoleHierarchy.GetHighestRole(g.Select(a => a.Role))
                });
            
            var result = allUsers.Select(u => new KeycloakUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                CreatedAt = u.CreatedAt,
                CollectionCount = userAclGroups.TryGetValue(u.Id, out var acl) ? acl.CollectionCount : 0,
                HighestRole = userAclGroups.TryGetValue(u.Id, out var acl2) ? acl2.HighestRole : null
            }).ToList();

            return Results.Ok(result);
        })
        .WithName("GetKeycloakUsers")
        .WithSummary("Gets all users from Keycloak (admin only)")
        .Produces<List<KeycloakUserDto>>();

        // ===== USER CREATION =====

        /// <summary>
        /// Creates a new user in Keycloak with optional initial collection access.
        /// </summary>
        group.MapPost("/users", async (
            [FromBody] CreateUserRequest request,
            [FromServices] IKeycloakUserService keycloakUserService,
            [FromServices] IUserProvisioningService provisioning,
            [FromServices] IConfiguration configuration,
            HttpContext httpContext,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var username = request.Username?.Trim() ?? "";
            var email = request.Email?.Trim() ?? "";
            var firstName = request.FirstName?.Trim() ?? "";
            var lastName = request.LastName?.Trim() ?? "";

            // Validate inputs using shared validators
            if (!InputValidation.TryValidate(out var errors,
                ("username", InputValidation.ValidateUsername(username)),
                ("email", InputValidation.ValidateEmail(email)),
                ("firstName", InputValidation.ValidateRequired(firstName, "First name")),
                ("lastName", InputValidation.ValidateRequired(lastName, "Last name")),
                ("password", InputValidation.ValidatePassword(request.Password))))
            {
                return Results.BadRequest(ApiError.ValidationError("Validation failed", errors));
            }

            // Validate initial collections exist
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
                return ex.StatusCode == 409
                    ? Results.Conflict(ApiError.BadRequest(ex.Message))
                    : Results.BadRequest(ApiError.BadRequest(ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error creating user '{Username}'", username);
                return Results.StatusCode(500);
            }
        })
        .WithName("CreateUser")
        .WithSummary("Creates a new user in Keycloak (admin only)")
        .Produces<CreateUserResponse>(StatusCodes.Status201Created)
        .Produces<ApiError>(StatusCodes.Status400BadRequest)
        .Produces<ApiError>(StatusCodes.Status409Conflict);
    }
}
