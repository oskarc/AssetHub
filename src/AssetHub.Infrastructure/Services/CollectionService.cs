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

        if (!currentUser.IsSystemAdmin)
        {
            var canCreate = await authService.CanCreateRootCollectionAsync(userId);
            if (!canCreate)
                return ServiceError.Forbidden();
        }

        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = descToStore,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
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
            UserRole = RoleHierarchy.Roles.Admin
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
}
