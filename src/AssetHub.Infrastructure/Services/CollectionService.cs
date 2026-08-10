using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Groups dependencies for <see cref="CollectionService"/>
/// to keep the constructor parameter count manageable.
/// </summary>
public sealed record CollectionServiceRepositories(
    ICollectionRepository CollectionRepo,
    ICollectionAclRepository AclRepo,
    IShareRepository ShareRepo,
    HybridCache Cache);

/// <summary>
/// Collection commands: create, update, delete, and download.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for collection commands: repos record + auth + deletion + zip + audit + UnitOfWork + IOptions + scoped CurrentUser. UnitOfWork added to wrap action+audit atomically (A-4).")]
public sealed class CollectionService(
    CollectionServiceRepositories repos,
    ICollectionAuthorizationService authService,
    IAssetDeletionService deletionService,
    IZipBuildService zipBuildService,
    IAuditService audit,
    IUnitOfWork uow,
    IOptions<MinIOSettings> minioSettings,
    CurrentUser currentUser) : ICollectionService
{
    private readonly string _bucketName = minioSettings.Value.BucketName;

    public async Task<ServiceResult<CollectionResponseDto>> CreateAsync(
        CreateCollectionDto dto, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 255)
            return ServiceError.BadRequest("Name must be 1-255 characters");

        if (dto.Description is not null && !string.IsNullOrWhiteSpace(dto.Description) && dto.Description.Length > 1000)
            return ServiceError.BadRequest("Description must be 1000 characters or fewer");

        var descToStore = InputValidation.NormalizeToNull(dto.Description);

        var nameExists = await repos.CollectionRepo.ExistsByNameAsync(dto.Name, ct: ct);
        if (nameExists)
            return ServiceError.BadRequest($"A collection named '{dto.Name}' already exists");

        // Nested collection creation is admin-only — same rule as SetParentAsync.
        // Otherwise contributors could bypass that gate by creating a child
        // under any parent they could name.
        if (dto.ParentCollectionId is not null && !currentUser.IsSystemAdmin)
            return ServiceError.Forbidden();

        if (!currentUser.IsSystemAdmin)
        {
            var canCreate = await authService.CanCreateRootCollectionAsync(userId);
            if (!canCreate)
                return ServiceError.Forbidden();
        }

        if (dto.ParentCollectionId is { } parentId)
        {
            if (!await repos.CollectionRepo.ExistsAsync(parentId, ct))
                return ServiceError.NotFound("Parent collection not found");
        }
        else if (dto.InheritParentAcl)
        {
            return ServiceError.BadRequest("Cannot enable inheritance: collection has no parent.");
        }

        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = descToStore,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            ParentCollectionId = dto.ParentCollectionId,
            InheritParentAcl = dto.InheritParentAcl,
        };

        try
        {
            // Insert + creator-admin ACL + audit atomic (A-4) — torn write
            // would leave a collection that nobody can manage or no audit
            // trail of who created it.
            await uow.ExecuteAsync(async tct =>
            {
                await repos.CollectionRepo.CreateAsync(collection, tct);
                await repos.AclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, userId, RoleHierarchy.Roles.Admin, tct);
                await audit.LogAsync("collection.created", Constants.ScopeTypes.Collection, collection.Id, userId,
                    new() { ["name"] = collection.Name },
                    tct);
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return ServiceError.Conflict($"A collection named '{dto.Name}' already exists");
        }

        await repos.Cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAccessTag(userId), ct);
        await repos.Cache.RemoveByTagAsync(CacheKeys.Tags.Dashboard, ct);

        return new CollectionResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            CreatedAt = collection.CreatedAt,
            CreatedByUserId = collection.CreatedByUserId,
            UserRole = RoleHierarchy.Roles.Admin,
            ParentCollectionId = collection.ParentCollectionId,
            InheritParentAcl = collection.InheritParentAcl,
        };
    }

    public async Task<ServiceResult<MessageResponse>> UpdateAsync(
        Guid id, UpdateCollectionDto dto, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var canUpdate = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Manager, ct);
        if (!canUpdate)
            return ServiceError.Forbidden();

        var collection = await repos.CollectionRepo.GetByIdAsync(id, ct: ct);
        if (collection is null)
            return ServiceError.NotFound("Collection not found");

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            if (dto.Name.Length > 255)
                return ServiceError.BadRequest("Name must be 1-255 characters");
            if (!string.Equals(collection.Name, dto.Name, StringComparison.OrdinalIgnoreCase))
            {
                var nameExists = await repos.CollectionRepo.ExistsByNameAsync(dto.Name, excludeId: id, ct: ct);
                if (nameExists)
                    return ServiceError.BadRequest($"A collection named '{dto.Name}' already exists");
            }
            collection.Name = dto.Name;
        }
        if (dto.Description is not null)
        {
            var desc = InputValidation.NormalizeToNull(dto.Description);
            if (desc is not null && desc.Length > 1000)
                return ServiceError.BadRequest("Description must be 1000 characters or fewer");
            collection.Description = desc;
        }

        try
        {
            // Update + audit atomic (A-4).
            await uow.ExecuteAsync(async tct =>
            {
                await repos.CollectionRepo.UpdateAsync(collection, tct);
                await audit.LogAsync("collection.updated", Constants.ScopeTypes.Collection, id, userId,
                    new() { ["name"] = collection.Name, ["description"] = collection.Description ?? "" },
                    tct);
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return ServiceError.Conflict($"A collection named '{dto.Name}' already exists");
        }

        await repos.Cache.RemoveByTagAsync(CacheKeys.Tags.Collection(id), ct);

        return new MessageResponse("Collection updated");
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var canDelete = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Admin, ct);
        if (!canDelete)
            return ServiceError.Forbidden();

        var collection = await repos.CollectionRepo.GetByIdAsync(id, ct: ct);
        if (collection is null)
            return ServiceError.NotFound("Collection not found");

        var collectionName = collection.Name;
        // Asset purge spans MinIO + DB and intentionally stays outside the
        // transaction below (best-effort cleanup).
        await deletionService.DeleteCollectionAssetsAsync(id, _bucketName, ct);

        // Share cleanup + collection delete + audit atomic (A-4) — torn write
        // here would leave dangling shares pointing at a deleted collection
        // or no audit trail of the deletion.
        await uow.ExecuteAsync(async tct =>
        {
            await repos.ShareRepo.DeleteByScopeAsync(Constants.ScopeTypes.Collection, id, tct);
            await repos.CollectionRepo.DeleteAsync(id, tct);
            await audit.LogAsync("collection.deleted", Constants.ScopeTypes.Collection, id, userId,
                new() { ["name"] = collectionName },
                tct);
        }, ct);

        await repos.Cache.RemoveByTagAsync(CacheKeys.Tags.Collection(id), ct);
        await repos.Cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAcl, ct);
        await repos.Cache.RemoveByTagAsync(CacheKeys.Tags.Dashboard, ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<ZipDownloadEnqueuedResponse>> DownloadAllAssetsAsync(
        Guid id, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var canView = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer, ct);
        if (!canView)
            return ServiceError.Forbidden();

        var exists = await repos.CollectionRepo.ExistsAsync(id, ct);
        if (!exists)
            return ServiceError.NotFound("Collection not found");

        await audit.LogAsync("collection.download_requested", Constants.ScopeTypes.Collection, id, userId, ct: ct);

        return await zipBuildService.EnqueueCollectionZipAsync(id, userId, ct);
    }

    /// <summary>
    /// Detects PostgreSQL unique constraint violations (error code 23505).
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";
    }

    // ── Nested collections (T5-NEST-01) ──────────────────────────────────

    public async Task<ServiceResult> SetParentAsync(Guid id, Guid? parentId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var collection = await repos.CollectionRepo.GetByIdAsync(id, ct: ct);
        if (collection is null) return ServiceError.NotFound("Collection not found");

        var previousParentId = collection.ParentCollectionId;
        if (previousParentId == parentId) return ServiceResult.Success;

        if (parentId is { } newParentId)
        {
            if (!await repos.CollectionRepo.ExistsAsync(newParentId, ct))
                return ServiceError.NotFound("Parent collection not found");

            var cycleError = await ValidateNoCycleAsync(id, newParentId, ct);
            if (cycleError is not null) return cycleError;
        }

        collection.ParentCollectionId = parentId;

        // Reparent + audit atomic (A-4). Cache cascade for the moving subtree
        // happens AFTER commit because the cache is read-after-write for the
        // very next request — we need the new parent FK to be visible first.
        await uow.ExecuteAsync(async tct =>
        {
            await repos.CollectionRepo.UpdateAsync(collection, tct);
            await audit.LogAsync(
                "collection.reparented",
                Constants.ScopeTypes.Collection,
                id,
                currentUser.UserId,
                new Dictionary<string, object>
                {
                    ["previous_parent_id"] = (object?)previousParentId ?? string.Empty,
                    ["new_parent_id"] = (object?)parentId ?? string.Empty,
                },
                tct);
        }, ct);

        await BustInheritingSubtreeCacheAsync(id, ct);
        return ServiceResult.Success;
    }

    public async Task<ServiceResult> SetInheritParentAclAsync(Guid id, bool inherit, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var collection = await repos.CollectionRepo.GetByIdAsync(id, ct: ct);
        if (collection is null) return ServiceError.NotFound("Collection not found");

        if (inherit && collection.ParentCollectionId is null)
            return ServiceError.BadRequest("Cannot enable inheritance: collection has no parent.");

        if (collection.InheritParentAcl == inherit) return ServiceResult.Success;

        collection.InheritParentAcl = inherit;
        var auditEvent = inherit ? "collection.inheritance_enabled" : "collection.inheritance_disabled";

        // Toggle + audit atomic (A-4). Cache cascade after commit.
        await uow.ExecuteAsync(async tct =>
        {
            await repos.CollectionRepo.UpdateAsync(collection, tct);
            await audit.LogAsync(
                auditEvent,
                Constants.ScopeTypes.Collection,
                id,
                currentUser.UserId,
                new Dictionary<string, object>
                {
                    ["parent_collection_id"] = (object?)collection.ParentCollectionId ?? string.Empty,
                },
                tct);
        }, ct);

        await BustInheritingSubtreeCacheAsync(id, ct);
        return ServiceResult.Success;
    }

    public async Task<ServiceResult<int>> CopyParentAclAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var collection = await repos.CollectionRepo.GetByIdAsync(id, includeAcls: true, ct: ct);
        if (collection is null) return ServiceError.NotFound("Collection not found");
        if (collection.ParentCollectionId is not Guid parentId)
            return ServiceError.BadRequest("Collection has no parent.");

        var parentAcls = await repos.AclRepo.GetByCollectionAsync(parentId, ct);
        var parentAclList = parentAcls.ToList();
        if (parentAclList.Count == 0) return 0;

        // Determine which parent rows aren't already on this collection.
        var existingPairs = collection.Acls
            .Select(a => (a.PrincipalType, a.PrincipalId))
            .ToHashSet();
        var toAdd = parentAclList
            .Where(a => !existingPairs.Contains((a.PrincipalType, a.PrincipalId)))
            .ToList();

        if (toAdd.Count == 0) return 0;

        // Per-principal SetAccessAsync + audit atomic (A-4). Each principal
        // upserts a new row on the child; existing rows on the child are
        // never mutated by this path.
        await uow.ExecuteAsync(async tct =>
        {
            foreach (var src in toAdd)
            {
                await repos.AclRepo.SetAccessAsync(
                    id,
                    src.PrincipalType.ToDbString(),
                    src.PrincipalId,
                    src.Role.ToDbString(),
                    tct);
            }
            await audit.LogAsync(
                "collection.acl_copied_from_parent",
                Constants.ScopeTypes.Collection,
                id,
                currentUser.UserId,
                new Dictionary<string, object>
                {
                    ["parent_collection_id"] = parentId,
                    ["principals_added"] = toAdd.Count,
                },
                tct);
        }, ct);

        // Cache: the child's effective ACL just grew, descendants that
        // inherit through it must refresh too.
        await BustInheritingSubtreeCacheAsync(id, ct);
        return toAdd.Count;
    }

    /// <summary>
    /// Validates that reparenting <paramref name="id"/> under
    /// <paramref name="newParentId"/> would neither form a cycle nor push the
    /// resulting chain past <see cref="Constants.Limits.MaxCollectionDepth"/>.
    /// The depth budget combines two pieces: the upward chain from the new
    /// parent (which becomes the new ancestor list above the moving collection)
    /// plus the subtree depth at the moving collection (its existing
    /// descendants come along for the ride).
    /// </summary>
    private async Task<ServiceError?> ValidateNoCycleAsync(Guid id, Guid? newParentId, CancellationToken ct)
    {
        if (newParentId is null) return null;
        if (newParentId == id) return ServiceError.BadRequest("A collection cannot be its own parent.");

        var ancestorDepth = 0;
        Guid? cursor = newParentId;
        while (cursor is not null)
        {
            ancestorDepth++;
            if (cursor == id)
                return ServiceError.BadRequest("Cycle detected: this would make the collection an ancestor of itself.");
            if (ancestorDepth > Constants.Limits.MaxCollectionDepth)
                return ServiceError.BadRequest($"Collection depth limit ({Constants.Limits.MaxCollectionDepth}) exceeded.");
            cursor = await repos.CollectionRepo.GetParentIdAsync(cursor.Value, ct);
        }

        var remaining = Constants.Limits.MaxCollectionDepth - ancestorDepth;
        var subtreeDepth = await repos.CollectionRepo.GetMaxSubtreeDepthAsync(id, remaining, ct);
        if (subtreeDepth > remaining)
            return ServiceError.BadRequest($"Collection depth limit ({Constants.Limits.MaxCollectionDepth}) exceeded.");

        return null;
    }

    /// <summary>
    /// Busts the cache for the changed collection itself plus any descendant
    /// that transitively inherits from it. Called post-commit so the new
    /// effective ACL is visible to the very next request.
    /// </summary>
    private async Task BustInheritingSubtreeCacheAsync(Guid changedId, CancellationToken ct)
    {
        await repos.Cache.RemoveByTagAsync(CacheKeys.Tags.Collection(changedId), ct);
        await repos.Cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAcl, ct);

        var inheritingDescendants = await repos.CollectionRepo.GetInheritingDescendantIdsAsync(changedId, ct);
        foreach (var descendantId in inheritingDescendants)
            await repos.Cache.RemoveByTagAsync(CacheKeys.Tags.Collection(descendantId), ct);
    }
}
