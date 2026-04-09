using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Groups repository dependencies for <see cref="CollectionAdminService"/>
/// to keep the constructor parameter count manageable.
/// </summary>
public sealed record CollectionAdminRepositories(
    ICollectionRepository CollectionRepo,
    ICollectionAclRepository AclRepo,
    IAssetCollectionRepository AssetCollectionRepo,
    IShareRepository ShareRepo);

/// <summary>
/// Admin-only bulk collection operations.
/// </summary>
public sealed class CollectionAdminService(
    CollectionAdminRepositories repos,
    IAssetDeletionService deletionService,
    IAuditService audit,
    IOptions<MinIOSettings> minioSettings,
    CurrentUser currentUser) : ICollectionAdminService
{
    private readonly string _bucketName = minioSettings.Value.BucketName;

    public async Task<ServiceResult<BulkDeleteCollectionsResponse>> BulkDeleteAsync(
        List<Guid> collectionIds, bool deleteAssets, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only system administrators can perform bulk operations");

        if (collectionIds.Count == 0)
            return ServiceError.BadRequest("No collections specified");

        var userId = currentUser.UserId;
        var deleted = 0;
        var errors = new List<BulkOperationError>();

        foreach (var id in collectionIds.Distinct())
        {
            try
            {
                var collection = await repos.CollectionRepo.GetByIdAsync(id, ct: ct);
                if (collection is null)
                {
                    errors.Add(new BulkOperationError { CollectionId = id, Error = "Collection not found" });
                    continue;
                }

                var collectionName = collection.Name;

                if (deleteAssets)
                    await deletionService.DeleteCollectionAssetsAsync(id, _bucketName, ct);
                else
                    await repos.AssetCollectionRepo.UnlinkAllFromCollectionAsync(id, ct);

                await repos.ShareRepo.DeleteByScopeAsync(Constants.ScopeTypes.Collection, id, ct);
                await repos.CollectionRepo.DeleteAsync(id, ct);

                await audit.LogAsync("collection.deleted", Constants.ScopeTypes.Collection, id, userId,
                    new() { ["name"] = collectionName, ["bulk"] = "true", ["deleteAssets"] = deleteAssets.ToString() }, ct);
                deleted++;
            }
            catch (Exception ex)
            {
                errors.Add(new BulkOperationError { CollectionId = id, Error = ex.Message });
            }
        }

        return new BulkDeleteCollectionsResponse
        {
            Message = $"Deleted {deleted} collection(s)",
            Deleted = deleted,
            Failed = errors.Count,
            Errors = errors
        };
    }

    public async Task<ServiceResult<BulkSetCollectionAccessResponse>> BulkSetAccessAsync(
        BulkSetCollectionAccessRequest request, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only system administrators can perform bulk operations");

        if (request.CollectionIds.Count == 0)
            return ServiceError.BadRequest("No collections specified");
        if (string.IsNullOrWhiteSpace(request.PrincipalId))
            return ServiceError.BadRequest("Principal ID is required");
        if (string.IsNullOrWhiteSpace(request.Role))
            return ServiceError.BadRequest("Role is required");

        var userId = currentUser.UserId;
        var updated = 0;
        var errors = new List<BulkOperationError>();

        foreach (var collectionId in request.CollectionIds.Distinct())
        {
            try
            {
                var collection = await repos.CollectionRepo.GetByIdAsync(collectionId, ct: ct);
                if (collection is null)
                {
                    errors.Add(new BulkOperationError { CollectionId = collectionId, Error = "Collection not found" });
                    continue;
                }

                await repos.AclRepo.SetAccessAsync(collectionId, request.PrincipalType, request.PrincipalId, request.Role, ct);

                await audit.LogAsync("collection.access_set", Constants.ScopeTypes.Collection, collectionId, userId,
                    new() { ["principalId"] = request.PrincipalId, ["role"] = request.Role, ["bulk"] = "true" }, ct);
                updated++;
            }
            catch (Exception ex)
            {
                errors.Add(new BulkOperationError { CollectionId = collectionId, Error = ex.Message });
            }
        }

        return new BulkSetCollectionAccessResponse
        {
            Message = $"Updated access on {updated} collection(s)",
            Updated = updated,
            Failed = errors.Count,
            Errors = errors
        };
    }
}
