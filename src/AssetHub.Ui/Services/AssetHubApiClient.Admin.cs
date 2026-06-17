using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<AdminSharesResponse> GetAllSharesAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await shareAdminService.GetAllSharesAsync(skip, take, ct);
        return Unwrap(result, "Get all shares");
    }

    public async Task RevokeShareAdminAsync(Guid id, CancellationToken ct = default)
    {
        var result = await shareAdminService.AdminRevokeShareAsync(id, ct);
        EnsureSuccess(result, "Revoke share");
    }

    public async Task DeleteShareAdminAsync(Guid id, CancellationToken ct = default)
    {
        var result = await shareAdminService.DeleteShareAsync(id, ct);
        EnsureSuccess(result, "Delete share");
    }

    public async Task<int> BulkDeleteSharesByStatusAsync(string status, CancellationToken ct = default)
    {
        var result = await shareAdminService.BulkDeleteSharesByStatusAsync(status, ct);
        return Unwrap(result, $"Bulk delete {status} shares");
    }

    public async Task<List<CollectionAccessDto>> GetCollectionAccessAsync(CancellationToken ct = default)
    {
        var result = await adminCollectionAclService.GetCollectionAccessTreeAsync(ct);
        return Unwrap(result, "Get collection access");
    }

    public async Task AddCollectionAclAsync(
        Guid collectionId,
        string principalType,
        string principalId,
        string role,
        CancellationToken ct = default)
    {
        var request = new SetCollectionAccessRequest
        {
            PrincipalType = principalType,
            PrincipalId = principalId,
            Role = role
        };
        Validate(request, "Add collection access");
        var result = await adminCollectionAclService.AdminSetAccessAsync(collectionId, request, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Add collection access");
    }

    public async Task UpdateCollectionAclAsync(
        Guid collectionId,
        string principalType,
        string principalId,
        string role,
        CancellationToken ct = default)
    {
        // Same endpoint as Add — set semantics handle both create and update.
        var request = new SetCollectionAccessRequest
        {
            PrincipalType = principalType,
            PrincipalId = principalId,
            Role = role
        };
        Validate(request, "Update collection access");
        var result = await adminCollectionAclService.AdminSetAccessAsync(collectionId, request, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Update collection access");
    }

    public async Task RemoveCollectionAclAsync(Guid collectionId, string principalId, string principalType, CancellationToken ct = default)
    {
        var result = await adminCollectionAclService.AdminRevokeAccessAsync(collectionId, principalType, principalId, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Remove collection access");
    }

    public async Task<BulkDeleteCollectionsResponse> BulkDeleteCollectionsAsync(List<Guid> collectionIds, bool deleteAssets = true, CancellationToken ct = default)
    {
        var result = await collectionAdminService.BulkDeleteAsync(collectionIds, deleteAssets, ct);
        return Unwrap(result, "Bulk delete collections");
    }

    public async Task<BulkSetCollectionAccessResponse> BulkSetCollectionAccessAsync(
        List<Guid> collectionIds, string principalId, string role, CancellationToken ct = default)
    {
        var request = new BulkSetCollectionAccessRequest
        {
            CollectionIds = collectionIds,
            PrincipalType = Constants.PrincipalTypes.User,
            PrincipalId = principalId,
            Role = role
        };
        Validate(request, "Bulk set collection access");
        var result = await collectionAdminService.BulkSetAccessAsync(request, ct);
        return Unwrap(result, "Bulk set collection access");
    }

    public async Task<List<UserAccessSummaryDto>> GetUsersAsync(CancellationToken ct = default)
    {
        var result = await userAdminQueryService.GetUsersAsync(ct);
        return Unwrap(result, "Get users");
    }

    public async Task<List<KeycloakUserDto>> GetKeycloakUsersAsync(CancellationToken ct = default)
    {
        var result = await userAdminQueryService.GetKeycloakUsersAsync(ct);
        return Unwrap(result, "Get Keycloak users");
    }

    public async Task<PaginatedKeycloakUsersResponse> GetKeycloakUsersPaginatedAsync(
        string? search = null, string? category = null,
        string? sortBy = null, bool sortDesc = false,
        int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await userAdminQueryService.GetKeycloakUsersPaginatedAsync(
            search, category, sortBy, sortDesc, skip, take, ct);
        return Unwrap(result, "Get Keycloak users (paginated)");
    }

    public async Task<CreateUserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        Validate(request, "Create user");
        var result = await userAdminService.CreateUserAsync(request, BaseUrl(), ct);
        return Unwrap(result, "Create user");
    }

    public async Task SendPasswordResetEmailAsync(string userId, CancellationToken ct = default)
    {
        var result = await userAdminService.SendPasswordResetEmailAsync(userId, ct);
        EnsureSuccess(result, "Send password reset email");
    }

    public async Task<DeleteUserResponse> DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        var result = await userAdminService.DeleteUserAsync(userId, ct);
        return Unwrap(result, "Delete user");
    }

    public async Task SetUserAdminAsync(string userId, bool isAdmin, CancellationToken ct = default)
    {
        var result = await userAdminService.SetAdminAsync(userId, isAdmin, ct);
        EnsureSuccess(result, isAdmin ? "Promote user to admin" : "Demote user from admin");
    }

    public async Task<UserSyncResult> SyncDeletedUsersAsync(bool dryRun = false, CancellationToken ct = default)
    {
        var result = await userAdminService.SyncDeletedUsersAsync(dryRun, ct);
        return Unwrap(result, "Sync deleted users");
    }

    public async Task<List<AuditEventDto>> GetAuditEventsAsync(int take = 200, CancellationToken ct = default)
    {
        var result = await auditQueryService.GetRecentAuditEventsAsync(take, ct);
        return Unwrap(result, "Get audit events");
    }

    public async Task<AuditQueryResponse> GetAuditEventsPaginatedAsync(
        int pageSize = 50,
        DateTime? cursor = null,
        string? eventType = null,
        string? targetType = null,
        string? actorUserId = null,
        CancellationToken ct = default)
    {
        var request = new AuditQueryRequest
        {
            PageSize = pageSize,
            Cursor = cursor,
            EventType = eventType,
            TargetType = targetType,
            ActorUserId = actorUserId
        };
        Validate(request, "Get audit events paginated");
        var result = await auditQueryService.GetAuditEventsAsync(request, ct);
        return Unwrap(result, "Get audit events paginated");
    }

    public async Task<List<ExportPresetDto>> GetExportPresetsAsync(CancellationToken ct = default)
    {
        var result = await exportPresetQueryService.GetAllAsync(ct);
        return Unwrap(result, "Get export presets");
    }

    public async Task<ExportPresetDto?> GetExportPresetAsync(Guid id, CancellationToken ct = default)
    {
        var result = await exportPresetQueryService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get export preset", 404);
    }

    public async Task<ExportPresetDto> CreateExportPresetAsync(CreateExportPresetDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create export preset");
        var result = await exportPresetService.CreateAsync(dto, ct);
        return Unwrap(result, "Create export preset");
    }

    public async Task UpdateExportPresetAsync(Guid id, UpdateExportPresetDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update export preset");
        var result = await exportPresetService.UpdateAsync(id, dto, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Update export preset");
    }

    public async Task DeleteExportPresetAsync(Guid id, CancellationToken ct = default)
    {
        var result = await exportPresetService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete export preset");
    }

    public async Task<TrashListResponse> GetTrashAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await assetTrashService.GetAsync(skip, take, ct);
        return Unwrap(result, "Get trash");
    }

    public async Task RestoreFromTrashAsync(Guid id, CancellationToken ct = default)
    {
        var result = await assetTrashService.RestoreAsync(id, ct);
        EnsureSuccess(result, "Restore from trash");
    }

    public async Task PurgeFromTrashAsync(Guid id, CancellationToken ct = default)
    {
        var result = await assetTrashService.PurgeAsync(id, ct);
        EnsureSuccess(result, "Purge from trash");
    }

    public async Task<EmptyTrashResponse> EmptyTrashAsync(CancellationToken ct = default)
    {
        var result = await assetTrashService.EmptyAsync(ct);
        return Unwrap(result, "Empty trash");
    }
}
