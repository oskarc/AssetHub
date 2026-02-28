using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Orchestrates collection CRUD and bulk download.
/// </summary>
public class CollectionService : ICollectionService
{
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICollectionAclRepository _aclRepo;
    private readonly IAssetRepository _assetRepo;
    private readonly IShareRepository _shareRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IAssetDeletionService _deletionService;
    private readonly IZipBuildService _zipBuildService;
    private readonly IAuditService _audit;
    private readonly string _bucketName;
    private readonly CurrentUser _currentUser;
    private readonly ILogger<CollectionService> _logger;

    public CollectionService(
        ICollectionRepository collectionRepo,
        ICollectionAclRepository aclRepo,
        IAssetRepository assetRepo,
        IShareRepository shareRepo,
        ICollectionAuthorizationService authService,
        IAssetDeletionService deletionService,
        IZipBuildService zipBuildService,
        IAuditService audit,
        IOptions<MinIOSettings> minioSettings,
        CurrentUser currentUser,
        ILogger<CollectionService> logger)
    {
        _collectionRepo = collectionRepo;
        _aclRepo = aclRepo;
        _assetRepo = assetRepo;
        _shareRepo = shareRepo;
        _authService = authService;
        _deletionService = deletionService;
        _zipBuildService = zipBuildService;
        _audit = audit;
        _bucketName = minioSettings.Value.BucketName;
        _currentUser = currentUser;
        _logger = logger;
    }

    private string BucketName => _bucketName;

    public async Task<ServiceResult<List<CollectionResponseDto>>> GetRootCollectionsAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var collections = await _collectionRepo.GetAccessibleCollectionsAsync(userId, ct);

        var dtos = await CollectionMapper.ToDtoListAsync(collections, userId, _authService, ct);
        return dtos;
    }

    public async Task<ServiceResult<CollectionResponseDto>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var hasAccess = await _authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer, ct);
        if (!hasAccess)
            return ServiceError.Forbidden();

        var collection = await _collectionRepo.GetByIdAsync(id, ct: ct);
        if (collection == null)
            return ServiceError.NotFound("Collection not found");

        var dto = await CollectionMapper.ToDtoAsync(collection, userId, _authService, ct);
        return dto;
    }

    public async Task<ServiceResult<CollectionResponseDto>> CreateAsync(
        CreateCollectionDto dto, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 255)
            return ServiceError.BadRequest("Name must be 1-255 characters");

        if (dto.Description != null && !string.IsNullOrWhiteSpace(dto.Description) && dto.Description.Length > 1000)
            return ServiceError.BadRequest("Description must be 1000 characters or fewer");

        var descToStore = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description;

        var nameExists = await _collectionRepo.ExistsByNameAsync(dto.Name, ct: ct);
        if (nameExists)
            return ServiceError.BadRequest($"A collection named '{dto.Name}' already exists");

        if (!_currentUser.IsSystemAdmin)
        {
            var canCreate = await _authService.CanCreateRootCollectionAsync(userId);
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

        await _collectionRepo.CreateAsync(collection, ct);
        await _aclRepo.SetAccessAsync(collection.Id, "user", userId, RoleHierarchy.Roles.Admin, ct);

        await _audit.LogAsync("collection.created", "collection", collection.Id, userId,
            new() { ["name"] = collection.Name },
            ct);

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
        var userId = _currentUser.UserId;

        var canUpdate = await _authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Manager, ct);
        if (!canUpdate)
            return ServiceError.Forbidden();

        var collection = await _collectionRepo.GetByIdAsync(id, ct: ct);
        if (collection == null)
            return ServiceError.NotFound("Collection not found");

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            if (dto.Name.Length > 255)
                return ServiceError.BadRequest("Name must be 1-255 characters");
            if (!string.Equals(collection.Name, dto.Name, StringComparison.OrdinalIgnoreCase))
            {
                var nameExists = await _collectionRepo.ExistsByNameAsync(dto.Name, excludeId: id, ct: ct);
                if (nameExists)
                    return ServiceError.BadRequest($"A collection named '{dto.Name}' already exists");
            }
            collection.Name = dto.Name;
        }
        if (dto.Description != null)
        {
            var desc = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description;
            if (desc != null && desc.Length > 1000)
                return ServiceError.BadRequest("Description must be 1000 characters or fewer");
            collection.Description = desc;
        }

        await _collectionRepo.UpdateAsync(collection, ct);
        await _audit.LogAsync("collection.updated", "collection", id, userId,
            new() { ["name"] = collection.Name, ["description"] = collection.Description ?? "" },
            ct);

        return new MessageResponse("Collection updated");
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var canDelete = await _authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Admin, ct);
        if (!canDelete)
            return ServiceError.Forbidden();

        var collection = await _collectionRepo.GetByIdAsync(id, ct: ct);
        if (collection == null)
            return ServiceError.NotFound("Collection not found");

        var collectionName = collection.Name;
        await _deletionService.DeleteCollectionAssetsAsync(id, BucketName, ct);
        await _shareRepo.DeleteByScopeAsync("collection", id, ct);
        await _collectionRepo.DeleteAsync(id, ct);

        await _audit.LogAsync("collection.deleted", "collection", id, userId,
            new() { ["name"] = collectionName },
            ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<ZipDownloadEnqueuedResponse>> DownloadAllAssetsAsync(
        Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var canView = await _authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer, ct);
        if (!canView)
            return ServiceError.Forbidden();

        var exists = await _collectionRepo.ExistsAsync(id, ct);
        if (!exists)
            return ServiceError.NotFound("Collection not found");

        await _audit.LogAsync("collection.download_requested", "collection", id, userId, ct: ct);

        return await _zipBuildService.EnqueueCollectionZipAsync(id, userId, ct);
    }
}
