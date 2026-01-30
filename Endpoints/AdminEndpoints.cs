using Dam.Application.Dtos;
using Dam.Application.Repositories;
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
            CancellationToken ct) =>
        {
            var shares = await shareRepo.GetAllAsync(includeAsset: true, includeCollection: true, ct);
            
            var result = shares.Select(s => new AdminShareDto
            {
                Id = s.Id,
                ScopeType = s.ScopeType,
                ScopeId = s.ScopeId,
                ScopeName = s.ScopeType == "asset" 
                    ? s.Asset?.Title ?? "Unknown Asset"
                    : s.Collection?.Name ?? "Unknown Collection",
                CreatedByUserId = s.CreatedByUserId,
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
                return Results.NotFound(new { error = "Share not found" });

            if (share.RevokedAt.HasValue)
                return Results.BadRequest(new { error = "Share is already revoked" });

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
            CancellationToken ct) =>
        {
            var collections = await collectionRepo.GetAllWithAclsAsync();
            
            // Build hierarchical structure
            var allCollections = collections.ToList();
            var rootCollections = allCollections.Where(c => c.ParentId == null).ToList();
            
            var result = rootCollections.Select(c => BuildCollectionAccessTree(c, allCollections)).ToList();
            
            return Results.Ok(result);
        })
        .WithName("GetCollectionAccess")
        .WithSummary("Gets all collections with ACLs in hierarchical structure (admin only)")
        .Produces<List<CollectionAccessDto>>();

        /// <summary>
        /// Sets access for a user on a collection.
        /// </summary>
        group.MapPost("/collections/{collectionId:guid}/acl", async (
            Guid collectionId,
            [FromBody] SetCollectionAccessRequest request,
            [FromServices] ICollectionRepository collectionRepo,
            [FromServices] ICollectionAclRepository aclRepo,
            CancellationToken ct) =>
        {
            if (!await collectionRepo.ExistsAsync(collectionId))
                return Results.NotFound(new { error = "Collection not found" });

            if (string.IsNullOrWhiteSpace(request.PrincipalId))
                return Results.BadRequest(new { error = "PrincipalId is required" });

            var validRoles = new[] { "viewer", "contributor", "manager", "admin" };
            if (!validRoles.Contains(request.Role?.ToLowerInvariant()))
                return Results.BadRequest(new { error = $"Invalid role. Must be one of: {string.Join(", ", validRoles)}" });

            var acl = await aclRepo.SetAccessAsync(
                collectionId, 
                request.PrincipalType ?? "user", 
                request.PrincipalId, 
                request.Role!.ToLowerInvariant());

            return Results.Ok(new { 
                message = "Access updated", 
                collectionId, 
                principalId = request.PrincipalId,
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
                return Results.NotFound(new { error = "Collection not found" });

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
            CancellationToken ct) =>
        {
            var allAcls = await aclRepo.GetAllAsync();
            var allCollections = (await collectionRepo.GetAllWithAclsAsync()).ToDictionary(c => c.Id);
            
            // Group by user
            var userAccess = allAcls
                .Where(a => a.PrincipalType == "user")
                .GroupBy(a => a.PrincipalId)
                .Select(g => new UserAccessSummaryDto
                {
                    UserId = g.Key,
                    CollectionCount = g.Count(),
                    HighestRole = GetHighestRole(g.Select(a => a.Role)),
                    Collections = g.Select(a => new UserCollectionAccessDto
                    {
                        CollectionId = a.CollectionId,
                        CollectionName = allCollections.TryGetValue(a.CollectionId, out var col) ? col.Name : "Unknown",
                        Role = a.Role
                    }).ToList()
                })
                .OrderBy(u => u.UserId)
                .ToList();

            return Results.Ok(userAccess);
        })
        .WithName("GetUsers")
        .WithSummary("Gets all users with collection access (admin only)")
        .Produces<List<UserAccessSummaryDto>>();
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
        List<Dam.Domain.Entities.Collection> allCollections)
    {
        var children = allCollections.Where(c => c.ParentId == collection.Id).ToList();
        
        return new CollectionAccessDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            ParentId = collection.ParentId,
            Acls = collection.Acls.Select(a => new CollectionAclDto
            {
                Id = a.Id,
                PrincipalType = a.PrincipalType,
                PrincipalId = a.PrincipalId,
                Role = a.Role
            }).ToList(),
            Children = children.Select(c => BuildCollectionAccessTree(c, allCollections)).ToList()
        };
    }

    private static string GetHighestRole(IEnumerable<string> roles)
    {
        var roleOrder = new Dictionary<string, int>
        {
            { "viewer", 1 },
            { "contributor", 2 },
            { "manager", 3 },
            { "admin", 4 }
        };

        return roles
            .OrderByDescending(r => roleOrder.TryGetValue(r.ToLowerInvariant(), out var order) ? order : 0)
            .FirstOrDefault() ?? "viewer";
    }
}
