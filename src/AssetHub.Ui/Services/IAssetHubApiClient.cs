using System.Diagnostics.CodeAnalysis;
using System.IO;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;

namespace AssetHub.Ui.Services;

/// <summary>
/// In-process facade surface consumed by the Blazor UI. Implemented by
/// <see cref="AssetHubApiClient"/>; mocked directly in component tests.
/// Methods return DTOs and throw <see cref="ApiException"/> on failure
/// (the ServiceResult -> exception translation happens in the implementation).
/// </summary>
[SuppressMessage("Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "Single facade surface for the UI — every domain DTO it exposes counts as a coupled type.")]
public interface IAssetHubApiClient
{
    Task<DashboardDto?> GetDashboardAsync(CancellationToken ct = default);
    Task<List<CollectionResponseDto>> GetCollectionsAsync(CancellationToken ct = default);
    Task<CollectionResponseDto?> GetCollectionAsync(Guid id, CancellationToken ct = default);
    Task<CollectionResponseDto> CreateCollectionAsync(CreateCollectionDto dto, CancellationToken ct = default);
    Task UpdateCollectionAsync(Guid id, UpdateCollectionDto dto, CancellationToken ct = default);
    Task DeleteCollectionAsync(Guid id, CancellationToken ct = default);
    Task<CollectionDeletionContextDto?> GetCollectionDeletionContextAsync(Guid id, CancellationToken ct = default);
    Task<List<CollectionAclResponseDto>> GetCollectionAclsAsync(Guid collectionId, CancellationToken ct = default);
    Task SetCollectionAccessAsync(Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default);
    Task RevokeCollectionAccessAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default);
    Task<List<UserSearchResultDto>> SearchUsersForAclAsync(Guid collectionId, string? query = null, CancellationToken ct = default);
    Task<AssetListResponse> GetAssetsAsync( Guid collectionId, string? query = null, string? type = null, string sortBy = Constants.SortBy.CreatedDesc, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<AssetResponseDto?> GetAssetAsync(Guid id, CancellationToken ct = default);
    Task<AssetResponseDto> UpdateAssetAsync(Guid id, UpdateAssetDto dto, CancellationToken ct = default);
    Task<AssetUploadResult> UploadAssetAsync( Guid collectionId, string title, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);
    Task<InitUploadResponse> InitUploadAsync( Guid? collectionId, string fileName, string contentType, long fileSize, string? title = null, CancellationToken ct = default);
    Task<AssetUploadResult> ConfirmUploadAsync(Guid assetId, CancellationToken ct = default);
    Task DeleteAssetAsync(Guid id, Guid? fromCollectionId = null, CancellationToken ct = default);
    Task<BulkDeleteAssetsResponse> BulkDeleteAssetsAsync( List<Guid> assetIds, Guid? fromCollectionId = null, CancellationToken ct = default);
    Task<AssetDeletionContextDto> GetAssetDeletionContextAsync(Guid id, CancellationToken ct = default);
    Task<ShareResponseDto> CreateShareAsync( Guid scopeId, string scopeType, DateTime? expiresAt = null, string? password = null, List<string>? notifyEmails = null, CancellationToken ct = default);
    Task UpdateSharePasswordAsync(Guid shareId, string newPassword, CancellationToken ct = default);
    Task<string> GetShareTokenAsync(Guid shareId, CancellationToken ct = default);
    Task<string?> GetSharePasswordAsync(Guid shareId, CancellationToken ct = default);
    Task RevokeShareAsync(Guid id, CancellationToken ct = default);
    Task<ISharedContentDto> GetSharedContentAsync( string token, string? password = null, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<ShareAccessTokenResponse?> GetShareAccessTokenAsync( string token, string password, CancellationToken ct = default);
    Task<string> GetPresignedDownloadUrlAsync(Guid assetId, string objectKey, CancellationToken ct = default);
    Task<AdminSharesResponse> GetAllSharesAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task RevokeShareAdminAsync(Guid id, CancellationToken ct = default);
    Task DeleteShareAdminAsync(Guid id, CancellationToken ct = default);
    Task<int> BulkDeleteSharesByStatusAsync(string status, CancellationToken ct = default);
    Task<List<CollectionAccessDto>> GetCollectionAccessAsync(CancellationToken ct = default);
    Task AddCollectionAclAsync( Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default);
    Task UpdateCollectionAclAsync( Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default);
    Task RemoveCollectionAclAsync(Guid collectionId, string principalId, string principalType, CancellationToken ct = default);
    Task<BulkDeleteCollectionsResponse> BulkDeleteCollectionsAsync(List<Guid> collectionIds, bool deleteAssets = true, CancellationToken ct = default);
    Task<BulkSetCollectionAccessResponse> BulkSetCollectionAccessAsync( List<Guid> collectionIds, string principalId, string role, CancellationToken ct = default);
    Task<List<UserAccessSummaryDto>> GetUsersAsync(CancellationToken ct = default);
    Task<List<DirectoryUserDto>> GetDirectoryUsersAsync(CancellationToken ct = default);
    Task<PaginatedDirectoryUsersResponse> GetDirectoryUsersPaginatedAsync( string? search = null, string? category = null, string? sortBy = null, bool sortDesc = false, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<CreateUserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task SendPasswordResetEmailAsync(string userId, CancellationToken ct = default);
    Task<DeleteUserResponse> DeleteUserAsync(string userId, CancellationToken ct = default);
    Task SetUserAdminAsync(string userId, bool isAdmin, CancellationToken ct = default);
    Task<List<AuditEventDto>> GetAuditEventsAsync(int take = 200, CancellationToken ct = default);
    Task<AuditQueryResponse> GetAuditEventsPaginatedAsync( int pageSize = 50, DateTime? cursor = null, string? eventType = null, string? targetType = null, string? actorUserId = null, CancellationToken ct = default);
    Task<List<AssetCollectionDto>> GetAssetCollectionsAsync(Guid assetId, CancellationToken ct = default);
    Task AddAssetToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default);
    Task RemoveAssetFromCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default);
    Task<AssetSearchResponse> SearchAssetsAsync(AssetSearchRequest request, CancellationToken ct = default);
    Task<TrashListResponse> GetTrashAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task RestoreFromTrashAsync(Guid id, CancellationToken ct = default);
    Task PurgeFromTrashAsync(Guid id, CancellationToken ct = default);
    Task<EmptyTrashResponse> EmptyTrashAsync(CancellationToken ct = default);
}
