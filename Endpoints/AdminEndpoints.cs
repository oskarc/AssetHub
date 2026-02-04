using Dam.Application;
using Dam.Application.Dtos;
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
                Status = GetShareStatus(s)
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
            var collections = await collectionRepo.GetAllWithAclsAsync();
            
            // Build hierarchical structure
            var allCollections = collections.ToList();
            var rootCollections = allCollections.Where(c => c.ParentId == null).ToList();
            
            // Lookup usernames for all principals
            var allUserIds = allCollections
                .SelectMany(c => c.Acls.Where(a => a.PrincipalType == "user").Select(a => a.PrincipalId))
                .Distinct()
                .ToList();
            var userNames = await userLookup.GetUserNamesAsync(allUserIds, ct);
            
            var result = rootCollections.Select(c => BuildCollectionAccessTree(c, allCollections, userNames)).ToList();
            
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
            if (!await collectionRepo.ExistsAsync(collectionId))
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
                request.Role!.ToLowerInvariant());

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
            if (!await collectionRepo.ExistsAsync(collectionId))
                return Results.NotFound(ApiError.NotFound("Collection not found"));

            await aclRepo.RevokeAccessAsync(collectionId, principalType ?? "user", principalId);

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
            var allAcls = await aclRepo.GetAllAsync();
            var allCollections = (await collectionRepo.GetAllWithAclsAsync()).ToDictionary(c => c.Id);
            
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
                    HighestRole = GetHighestRole(g.Select(a => a.Role)),
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
            var allAcls = await aclRepo.GetAllAsync();
            
            // Group ACLs by user to get collection count and highest role
            var userAclGroups = allAcls
                .Where(a => a.PrincipalType == "user")
                .GroupBy(a => a.PrincipalId)
                .ToDictionary(g => g.Key, g => new
                {
                    CollectionCount = g.Count(),
                    HighestRole = GetHighestRole(g.Select(a => a.Role))
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
    }

    // ===== HELPER METHODS =====
    
    private static string GetShareStatus(Dam.Domain.Entities.Share share)
    {
        if (share.RevokedAt.HasValue)
            return "Revoked";
        if (share.ExpiresAt < DateTime.UtcNow)
            return "Expired";
        return "Active";
    }

    private static CollectionAccessDto BuildCollectionAccessTree(
        Dam.Domain.Entities.Collection collection, 
        List<Dam.Domain.Entities.Collection> allCollections,
        Dictionary<string, string> userNames)
    {
        var children = allCollections.Where(c => c.ParentId == collection.Id).ToList();
        
        return new CollectionAccessDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            ParentId = collection.ParentId,
            Acls = collection.Acls.Select(a => new CollectionAclResponseDto
            {
                Id = a.Id,
                PrincipalType = a.PrincipalType,
                PrincipalId = a.PrincipalId,
                PrincipalName = a.PrincipalType == "user" && userNames.TryGetValue(a.PrincipalId, out var name) 
                    ? name 
                    : a.PrincipalId,
                Role = a.Role
            }).ToList(),
            Children = children.Select(c => BuildCollectionAccessTree(c, allCollections, userNames)).ToList()
        };
    }

    private static string GetHighestRole(IEnumerable<string> roles)
    {
        return roles
            .OrderByDescending(r => RoleHierarchy.GetLevel(r))
            .FirstOrDefault() ?? RoleHierarchy.Roles.Viewer;
    }
}
