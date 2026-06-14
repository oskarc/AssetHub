using System.Diagnostics.CodeAnalysis;
using System.IO;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Watermarking;

namespace AssetHub.Ui.Services;

/// <summary>
/// Optional parameters for <see cref="IAssetHubApiClient.ApplyEditAsync"/>.
/// </summary>
public sealed record ImageEditOptions(
    string? Title = null,
    string? EditDocument = null,
    Guid? DestinationCollectionId = null,
    Guid[]? PresetIds = null);

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
    Task SetCollectionParentAsync(Guid collectionId, Guid? parentId, CancellationToken ct = default);
    Task SetCollectionInheritParentAclAsync(Guid collectionId, bool inherit, CancellationToken ct = default);
    Task<int> CopyCollectionAclFromParentAsync(Guid collectionId, CancellationToken ct = default);
    Task<List<CollectionAclResponseDto>> GetCollectionAclsAsync(Guid collectionId, CancellationToken ct = default);
    Task SetCollectionAccessAsync(Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default);
    Task RevokeCollectionAccessAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default);
    Task<List<UserSearchResultDto>> SearchUsersForAclAsync(Guid collectionId, string? query = null, CancellationToken ct = default);
    Task<AssetListResponse> GetAssetsAsync( Guid collectionId, string? query = null, string? type = null, string sortBy = Constants.SortBy.CreatedDesc, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<AssetResponseDto?> GetAssetAsync(Guid id, CancellationToken ct = default);
    Task<AssetResponseDto> UpdateAssetAsync(Guid id, UpdateAssetDto dto, CancellationToken ct = default);
    Task<AssetUploadResult> UploadAssetAsync( Guid collectionId, string title, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);
    Task<InitUploadResponse> InitUploadAsync( Guid? collectionId, string fileName, string contentType, long fileSize, string? title = null, CancellationToken ct = default);
    Task<AssetUploadResult> ConfirmUploadAsync(Guid assetId, bool force = false, CancellationToken ct = default);
    Task<InitUploadResponse> SaveImageCopyAsync( Guid sourceAssetId, string contentType, long fileSize, string? title = null, Guid? collectionId = null, CancellationToken ct = default);
    Task<InitUploadResponse> ReplaceImageFileAsync( Guid assetId, string contentType, long fileSize, CancellationToken ct = default);
    Task<ImageEditResultDto> ApplyEditAsync( Guid assetId, Stream renderedPng, string fileName, ImageEditSaveMode saveMode, ImageEditOptions? options = null, CancellationToken ct = default);
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
    Task<List<KeycloakUserDto>> GetKeycloakUsersAsync(CancellationToken ct = default);
    Task<PaginatedKeycloakUsersResponse> GetKeycloakUsersPaginatedAsync( string? search = null, string? category = null, string? sortBy = null, bool sortDesc = false, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<CreateUserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task SendPasswordResetEmailAsync(string userId, CancellationToken ct = default);
    Task<DeleteUserResponse> DeleteUserAsync(string userId, CancellationToken ct = default);
    Task SetUserAdminAsync(string userId, bool isAdmin, CancellationToken ct = default);
    Task<UserSyncResult> SyncDeletedUsersAsync(bool dryRun = false, CancellationToken ct = default);
    Task<List<AuditEventDto>> GetAuditEventsAsync(int take = 200, CancellationToken ct = default);
    Task<AuditQueryResponse> GetAuditEventsPaginatedAsync( int pageSize = 50, DateTime? cursor = null, string? eventType = null, string? targetType = null, string? actorUserId = null, CancellationToken ct = default);
    Task<List<ExportPresetDto>> GetExportPresetsAsync(CancellationToken ct = default);
    Task<ExportPresetDto?> GetExportPresetAsync(Guid id, CancellationToken ct = default);
    Task<ExportPresetDto> CreateExportPresetAsync(CreateExportPresetDto dto, CancellationToken ct = default);
    Task UpdateExportPresetAsync(Guid id, UpdateExportPresetDto dto, CancellationToken ct = default);
    Task DeleteExportPresetAsync(Guid id, CancellationToken ct = default);
    Task<List<PersonalAccessTokenDto>> GetMyPersonalAccessTokensAsync(CancellationToken ct = default);
    Task<CreatedPersonalAccessTokenDto> CreatePersonalAccessTokenAsync( CreatePersonalAccessTokenRequest request, CancellationToken ct = default);
    Task RevokePersonalAccessTokenAsync(Guid id, CancellationToken ct = default);
    Task<NotificationListResponse> GetNotificationsAsync( bool unreadOnly = false, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<int> GetNotificationUnreadCountAsync(CancellationToken ct = default);
    Task MarkNotificationReadAsync(Guid id, CancellationToken ct = default);
    Task<int> MarkAllNotificationsReadAsync(CancellationToken ct = default);
    Task DeleteNotificationAsync(Guid id, CancellationToken ct = default);
    Task<NotificationPreferencesDto> GetNotificationPreferencesAsync(CancellationToken ct = default);
    Task<NotificationPreferencesDto> UpdateNotificationPreferencesAsync( UpdateNotificationPreferencesDto dto, CancellationToken ct = default);
    Task RotateUnsubscribeTokenAsync(CancellationToken ct = default);
    Task<MigrationListResponse> GetMigrationsAsync(int skip = 0, int take = 20, CancellationToken ct = default);
    Task<MigrationResponseDto> GetMigrationAsync(Guid id, CancellationToken ct = default);
    Task<MigrationResponseDto> CreateMigrationAsync(CreateMigrationDto dto, CancellationToken ct = default);
    Task UploadMigrationManifestAsync(Guid id, Stream csvStream, string fileName, CancellationToken ct = default);
    Task UploadMigrationFilesAsync(Guid id, IEnumerable<(string FileName, Stream Stream, string ContentType)> files, CancellationToken ct = default);
    Task StartMigrationAsync(Guid id, CancellationToken ct = default);
    Task StartMigrationS3ScanAsync(Guid id, CancellationToken ct = default);
    Task CancelMigrationAsync(Guid id, CancellationToken ct = default);
    Task RetryFailedMigrationAsync(Guid id, CancellationToken ct = default);
    Task<MigrationProgressDto> GetMigrationProgressAsync(Guid id, CancellationToken ct = default);
    Task<MigrationItemListResponse> GetMigrationItemsAsync(Guid id, string? statusFilter = null, int skip = 0, int take = 50, CancellationToken ct = default);
    Task DeleteMigrationAsync(Guid id, CancellationToken ct = default);
    Task<Stream> DownloadMigrationOutcomeAsync(Guid id, CancellationToken ct = default);
    Task UnstageMigrationItemAsync(Guid migrationId, Guid itemId, CancellationToken ct = default);
    Task<int> BulkDeleteMigrationsAsync(string filter, CancellationToken ct = default);
    Task<List<AssetCollectionDto>> GetAssetCollectionsAsync(Guid assetId, CancellationToken ct = default);
    Task<List<AssetDerivativeDto>> GetAssetDerivativesAsync(Guid assetId, CancellationToken ct = default);
    Task AddAssetToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default);
    Task RemoveAssetFromCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default);
    Task<List<MetadataSchemaDto>> GetMetadataSchemasAsync(CancellationToken ct = default);
    Task<MetadataSchemaDto?> GetMetadataSchemaAsync(Guid id, CancellationToken ct = default);
    Task<List<MetadataSchemaDto>> GetApplicableMetadataSchemasAsync(string? assetType = null, Guid? collectionId = null, CancellationToken ct = default);
    Task<MetadataSchemaDto> CreateMetadataSchemaAsync(CreateMetadataSchemaDto dto, CancellationToken ct = default);
    Task<MetadataSchemaDto> UpdateMetadataSchemaAsync(Guid id, UpdateMetadataSchemaDto dto, CancellationToken ct = default);
    Task DeleteMetadataSchemaAsync(Guid id, bool force = false, CancellationToken ct = default);
    Task<List<TaxonomySummaryDto>> GetTaxonomiesAsync(CancellationToken ct = default);
    Task<TaxonomyDto?> GetTaxonomyAsync(Guid id, CancellationToken ct = default);
    Task<TaxonomyDto> CreateTaxonomyAsync(CreateTaxonomyDto dto, CancellationToken ct = default);
    Task<TaxonomyDto> UpdateTaxonomyAsync(Guid id, UpdateTaxonomyDto dto, CancellationToken ct = default);
    Task<TaxonomyDto> ReplaceTaxonomyTermsAsync(Guid id, List<UpsertTaxonomyTermDto> terms, CancellationToken ct = default);
    Task DeleteTaxonomyAsync(Guid id, CancellationToken ct = default);
    Task<List<AssetMetadataValueDto>> GetAssetMetadataAsync(Guid assetId, CancellationToken ct = default);
    Task<List<AssetMetadataValueDto>> SetAssetMetadataAsync(Guid assetId, SetAssetMetadataDto dto, CancellationToken ct = default);
    Task<AssetSearchResponse> SearchAssetsAsync(AssetSearchRequest request, CancellationToken ct = default);
    Task<List<SavedSearchDto>> GetSavedSearchesAsync(CancellationToken ct = default);
    Task<SavedSearchDto?> GetSavedSearchAsync(Guid id, CancellationToken ct = default);
    Task<SavedSearchDto> CreateSavedSearchAsync(CreateSavedSearchDto dto, CancellationToken ct = default);
    Task<SavedSearchDto> UpdateSavedSearchAsync(Guid id, UpdateSavedSearchDto dto, CancellationToken ct = default);
    Task DeleteSavedSearchAsync(Guid id, CancellationToken ct = default);
    Task<TrashListResponse> GetTrashAsync(int skip = 0, int take = 50, CancellationToken ct = default);
    Task RestoreFromTrashAsync(Guid id, CancellationToken ct = default);
    Task PurgeFromTrashAsync(Guid id, CancellationToken ct = default);
    Task<EmptyTrashResponse> EmptyTrashAsync(CancellationToken ct = default);
    Task<List<AssetVersionDto>> GetAssetVersionsAsync(Guid assetId, CancellationToken ct = default);
    Task<AssetVersionDto> RestoreAssetVersionAsync(Guid assetId, int versionNumber, CancellationToken ct = default);
    Task PruneAssetVersionAsync(Guid assetId, int versionNumber, CancellationToken ct = default);
    Task<List<AssetCommentResponseDto>> GetAssetCommentsAsync(Guid assetId, CancellationToken ct = default);
    Task<AssetCommentResponseDto> CreateAssetCommentAsync( Guid assetId, CreateAssetCommentDto dto, CancellationToken ct = default);
    Task<AssetCommentResponseDto> UpdateAssetCommentAsync( Guid assetId, Guid commentId, UpdateAssetCommentDto dto, CancellationToken ct = default);
    Task DeleteAssetCommentAsync(Guid assetId, Guid commentId, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetUserNamesAsync( IEnumerable<string> userIds, CancellationToken ct = default);
    Task<List<UserSearchResultDto>> SearchUsersForMentionAsync( string query, int take = 10, CancellationToken ct = default);
    Task<AssetWorkflowResponseDto> GetAssetWorkflowAsync(Guid assetId, CancellationToken ct = default);
    Task<AssetWorkflowResponseDto> SubmitAssetForReviewAsync(Guid assetId, string? reason, CancellationToken ct = default);
    Task<AssetWorkflowResponseDto> ApproveAssetAsync(Guid assetId, string? reason, CancellationToken ct = default);
    Task<AssetWorkflowResponseDto> RejectAssetAsync(Guid assetId, string reason, CancellationToken ct = default);
    Task<AssetWorkflowResponseDto> PublishAssetAsync(Guid assetId, string? reason, CancellationToken ct = default);
    Task<AssetWorkflowResponseDto> UnpublishAssetAsync(Guid assetId, string? reason, CancellationToken ct = default);
    Task<List<WebhookResponseDto>> GetWebhooksAsync(CancellationToken ct = default);
    Task<CreatedWebhookDto> CreateWebhookAsync(CreateWebhookDto dto, CancellationToken ct = default);
    Task<WebhookResponseDto> UpdateWebhookAsync(Guid id, UpdateWebhookDto dto, CancellationToken ct = default);
    Task DeleteWebhookAsync(Guid id, CancellationToken ct = default);
    Task<CreatedWebhookDto> RotateWebhookSecretAsync(Guid id, CancellationToken ct = default);
    Task<WebhookDeliveryResponseDto> SendWebhookTestAsync(Guid id, CancellationToken ct = default);
    Task<List<WebhookDeliveryResponseDto>> GetWebhookDeliveriesAsync( Guid id, int take = 50, CancellationToken ct = default);
    Task<List<BrandResponseDto>> GetBrandsAsync(CancellationToken ct = default);
    Task<BrandResponseDto> CreateBrandAsync(CreateBrandDto dto, CancellationToken ct = default);
    Task<BrandResponseDto> UpdateBrandAsync(Guid id, UpdateBrandDto dto, CancellationToken ct = default);
    Task DeleteBrandAsync(Guid id, CancellationToken ct = default);
    Task<BrandResponseDto> UploadBrandLogoAsync( Guid id, Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task RemoveBrandLogoAsync(Guid id, CancellationToken ct = default);
    Task AssignBrandToCollectionAsync(Guid brandId, Guid collectionId, CancellationToken ct = default);
    Task UnassignBrandFromCollectionAsync(Guid collectionId, CancellationToken ct = default);
    Task<List<GuestInvitationResponseDto>> GetGuestInvitationsAsync(CancellationToken ct = default);
    Task<CreatedGuestInvitationDto> CreateGuestInvitationAsync( CreateGuestInvitationDto dto, CancellationToken ct = default);
    Task RevokeGuestInvitationAsync(Guid id, CancellationToken ct = default);
    Task<AcceptGuestInvitationResponseDto> AcceptGuestInvitationAsync( string token, CancellationToken ct = default);
    Task SetCollectionWatermarkAsync( Guid collectionId, bool enabled, CancellationToken ct = default);
    Task SetAssetWatermarkOverrideAsync( Guid assetId, bool? @override, CancellationToken ct = default);
    Task SetShareWatermarkOverrideAsync( Guid shareId, bool? @override, CancellationToken ct = default);
    Task<WatermarkVerificationResultDto> VerifyWatermarkAsync( Stream content, string contentType, long sizeBytes, CancellationToken ct = default);
    Task<IReadOnlyList<AnalyticsAssetDownloadRowDto>> GetTopDownloadedAssetsAsync( int windowDays, int take, CancellationToken ct = default);
    Task<IReadOnlyList<AnalyticsDailyPointDto>> GetDailyDownloadCountsAsync( int windowDays, CancellationToken ct = default);
    Task<IReadOnlyList<AnalyticsStorageByCollectionRowDto>> GetStorageByCollectionAsync( int take, CancellationToken ct = default);
    Task<IReadOnlyList<AnalyticsStorageByAssetTypeRowDto>> GetStorageByAssetTypeAsync( CancellationToken ct = default);
    Task<IReadOnlyList<AnalyticsExposureRowDto>> GetTopRecipientsAsync( int windowDays, int take, CancellationToken ct = default);
    Task<RevealRecipientResponseDto> RevealRecipientAsync( RevealRecipientRequestDto request, CancellationToken ct = default);
    Task<AnalyticsPdfJobStatusDto> EnqueueAnalyticsPdfAsync( int windowDays, CancellationToken ct = default);
    Task<AnalyticsPdfJobStatusDto> GetAnalyticsPdfStatusAsync( Guid jobId, CancellationToken ct = default);
}
