using AssetHub.Application;
using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<List<CollectionResponseDto>> GetCollectionsAsync(CancellationToken ct = default)
    {
        var result = await collectionQueryService.GetRootCollectionsAsync(ct);
        return Unwrap(result, "Get collections");
    }

    public async Task<CollectionResponseDto?> GetCollectionAsync(Guid id, CancellationToken ct = default)
    {
        var result = await collectionQueryService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get collection", 404);
    }

    public async Task<CollectionResponseDto> CreateCollectionAsync(CreateCollectionDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create collection");
        var result = await collectionService.CreateAsync(dto, ct);
        return Unwrap(result, "Create collection");
    }

    public async Task UpdateCollectionAsync(Guid id, UpdateCollectionDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update collection");
        var result = await collectionService.UpdateAsync(id, dto, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Update collection");
    }

    public async Task DeleteCollectionAsync(Guid id, CancellationToken ct = default)
    {
        var result = await collectionService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete collection");
    }

    public async Task<CollectionDeletionContextDto?> GetCollectionDeletionContextAsync(Guid id, CancellationToken ct = default)
    {
        var result = await collectionQueryService.GetDeletionContextAsync(id, ct);
        return UnwrapOrNullOn(result, "Get collection deletion context", 403, 404);
    }

    public async Task SetCollectionParentAsync(Guid collectionId, Guid? parentId, CancellationToken ct = default)
    {
        var result = await collectionService.SetParentAsync(collectionId, parentId, ct);
        EnsureSuccess(result, "Set collection parent");
    }

    public async Task SetCollectionInheritParentAclAsync(Guid collectionId, bool inherit, CancellationToken ct = default)
    {
        var result = await collectionService.SetInheritParentAclAsync(collectionId, inherit, ct);
        EnsureSuccess(result, "Set collection inherit-acl");
    }

    public async Task<int> CopyCollectionAclFromParentAsync(Guid collectionId, CancellationToken ct = default)
    {
        var result = await collectionService.CopyParentAclAsync(collectionId, ct);
        return Unwrap(result, "Copy collection ACL from parent");
    }

    public async Task<List<CollectionAclResponseDto>> GetCollectionAclsAsync(Guid collectionId, CancellationToken ct = default)
    {
        var result = await collectionAclService.GetAclsAsync(collectionId, ct);
        return Unwrap(result, "Get collection ACLs").ToList();
    }

    public async Task SetCollectionAccessAsync(Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default)
    {
        var result = await collectionAclService.SetAccessAsync(collectionId, principalType, principalId, role, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Set collection access");
    }

    public async Task RevokeCollectionAccessAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default)
    {
        var result = await collectionAclService.RevokeAccessAsync(collectionId, principalType, principalId, ct);
        EnsureSuccess(result, "Revoke collection access");
    }

    public async Task<List<UserSearchResultDto>> SearchUsersForAclAsync(Guid collectionId, string? query = null, CancellationToken ct = default)
    {
        var result = await collectionAclService.SearchUsersForAclAsync(collectionId, query, ct);
        return Unwrap(result, "Search users for ACL");
    }
}
