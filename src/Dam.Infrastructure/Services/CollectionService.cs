using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dam.Infrastructure.Services;

/// <summary>
/// Orchestrates collection CRUD, child navigation, and bulk download.
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
    private readonly IConfiguration _configuration;
    private readonly CurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
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
        IConfiguration configuration,
        CurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
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
        _configuration = configuration;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    private string BucketName => StorageConfig.GetBucketName(_configuration);
    private HttpContext? HttpCtx => _httpContextAccessor.HttpContext;

    public async Task<ServiceResult<List<CollectionResponseDto>>> GetRootCollectionsAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var collections = await _collectionRepo.GetAccessibleCollectionsAsync(userId, ct);

        var accessibleIds = collections.Select(c => c.Id).ToHashSet();
        var entryPoints = collections
            .Where(c => c.ParentId == null || !accessibleIds.Contains(c.ParentId.Value));

        var dtos = await CollectionMapper.ToDtoListAsync(entryPoints, userId, _authService, ct);
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

        if (!dto.ParentId.HasValue)
        {
            var nameExists = await _collectionRepo.ExistsByNameAsync(dto.Name, ct: ct);
            if (nameExists)
                return ServiceError.BadRequest($"A top-level collection named '{dto.Name}' already exists");
        }

        if (dto.ParentId.HasValue)
        {
            var canCreate = await _authService.CanCreateSubCollectionAsync(userId, dto.ParentId.Value, ct);
            if (!canCreate)
                return ServiceError.Forbidden();
        }
        else
        {
            if (!_currentUser.IsSystemAdmin)
            {
                var canCreate = await _authService.CanCreateRootCollectionAsync(userId);
                if (!canCreate)
                    return ServiceError.Forbidden();
            }
        }

        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            ParentId = dto.ParentId,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        await _collectionRepo.CreateAsync(collection, ct);
        await _aclRepo.SetAccessAsync(collection.Id, "user", userId, RoleHierarchy.Roles.Admin, ct);

        await _audit.LogAsync("collection.created", "collection", collection.Id, userId,
            new() { ["name"] = collection.Name, ["parentId"] = (object?)collection.ParentId ?? "root" },
            ct);

        return new CollectionResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            ParentId = collection.ParentId,
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
            if (collection.ParentId == null && !string.Equals(collection.Name, dto.Name, StringComparison.OrdinalIgnoreCase))
            {
                var nameExists = await _collectionRepo.ExistsByNameAsync(dto.Name, excludeId: id, ct: ct);
                if (nameExists)
                    return ServiceError.BadRequest($"A top-level collection named '{dto.Name}' already exists");
            }
            collection.Name = dto.Name;
        }
        if (dto.Description != null)
            collection.Description = dto.Description;

        await _collectionRepo.UpdateAsync(collection, ct);
        await _audit.LogAsync("collection.updated", "collection", id, userId, ct: ct);

        return new MessageResponse("Collection updated");
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var canDelete = await _authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Admin, ct);
        if (!canDelete)
            return ServiceError.Forbidden();

        var exists = await _collectionRepo.ExistsAsync(id, ct);
        if (!exists)
            return ServiceError.NotFound("Collection not found");

        await _deletionService.DeleteCollectionAssetsAsync(id, BucketName, ct);
        await _shareRepo.DeleteByScopeAsync("collection", id, ct);
        await _collectionRepo.DeleteAsync(id, ct);

        await _audit.LogAsync("collection.deleted", "collection", id, userId, ct: ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<List<CollectionResponseDto>>> GetChildrenAsync(
        Guid parentId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var hasAccess = await _authService.CheckAccessAsync(userId, parentId, RoleHierarchy.Roles.Viewer, ct);
        if (!hasAccess)
            return ServiceError.Forbidden();

        var children = await _collectionRepo.GetChildrenAsync(parentId, ct);
        var dtos = await CollectionMapper.ToDtoListAsync(children, userId, _authService, ct);
        return dtos;
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

        return await _zipBuildService.EnqueueCollectionZipAsync(id, userId, ct);
    }
}
