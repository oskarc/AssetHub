using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.Extensions.Options;

namespace AssetHub.Ui.Services;

/// <summary>
/// In-process facade over AssetHub application services. Preserves the legacy
/// "API client" surface (DTO-or-throw <see cref="ApiException"/>) so Razor pages
/// keep their existing call sites, while eliminating the HTTP loopback the
/// original HttpClient-based implementation forced.
///
/// Errors from <see cref="ServiceResult{T}"/> are translated into <see cref="ApiException"/>
/// with the matching status code, error code, and details — same shape callers
/// already handle. DTO inputs are validated against their DataAnnotations before
/// being passed to services (replicating <c>ValidationFilter&lt;T&gt;</c> on endpoints).
/// </summary>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for the UI's API surface — every domain service it routes to is one constructor parameter.")]
[SuppressMessage("Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "Single facade for the UI — every domain service it routes to counts as a coupled type.")]
public class AssetHubApiClient(
    IDashboardService dashboardService,
    ICollectionQueryService collectionQueryService,
    ICollectionService collectionService,
    ICollectionAclService collectionAclService,
    IAdminCollectionAclService adminCollectionAclService,
    ICollectionAdminService collectionAdminService,
    IAssetService assetService,
    IAssetQueryService assetQueryService,
    IAssetUploadService assetUploadService,
    IImageEditingService imageEditingService,
    IAssetMetadataService assetMetadataService,
    IAssetSearchService assetSearchService,
    ISavedSearchService savedSearchService,
    IAssetTrashService assetTrashService,
    IAssetVersionService assetVersionService,
    IAssetCommentService assetCommentService,
    IAssetWorkflowService assetWorkflowService,
    IAuthenticatedShareAccessService authShareAccessService,
    IPublicShareAccessService publicShareAccessService,
    IShareAdminService shareAdminService,
    IUserAdminQueryService userAdminQueryService,
    IUserAdminService userAdminService,
    IAuditQueryService auditQueryService,
    IExportPresetQueryService exportPresetQueryService,
    IExportPresetService exportPresetService,
    IPersonalAccessTokenService personalAccessTokenService,
    INotificationService notificationService,
    INotificationPreferencesService notificationPreferencesService,
    IMigrationService migrationService,
    IMetadataSchemaService metadataSchemaService,
    IMetadataSchemaQueryService metadataSchemaQueryService,
    ITaxonomyService taxonomyService,
    ITaxonomyQueryService taxonomyQueryService,
    IWebhookService webhookService,
    IBrandService brandService,
    IGuestInvitationService guestInvitationService,
    IOptions<AppSettings> appSettings)
{
    private readonly AppSettings _appSettings = appSettings.Value;

    /// <summary>
    /// Test-only constructor. Castle DynamicProxy (used by Moq) needs a parameterless
    /// constructor to subclass this type for mocking. Production code MUST use the
    /// primary constructor — this one leaves every backing service null and any
    /// non-mocked virtual call would NRE. <see cref="EditorBrowsableAttribute"/> hides
    /// it from IntelliSense to discourage accidental use.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    protected AssetHubApiClient()
        : this(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
               null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
               null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
               null!, null!, null!, null!, null!, null!, Options.Create(new AppSettings()))
    {
    }

    #region Helpers

    /// <summary>Unwrap a value-bearing result. Throws <see cref="ApiException"/> on failure.</summary>
    private static T Unwrap<T>(ServiceResult<T> result, string operation)
    {
        if (result.IsSuccess && result.Value is not null) return result.Value;
        if (result.IsSuccess)
            throw new ApiException($"{operation} returned an empty response", HttpStatusCode.InternalServerError);
        throw ToApiException(result.Error!, operation);
    }

    /// <summary>Unwrap a result, returning null when the error status matches one of the listed codes.</summary>
    private static T? UnwrapOrNullOn<T>(ServiceResult<T> result, string operation, params int[] nullStatusCodes)
        where T : class
    {
        if (result.IsSuccess) return result.Value;
        if (nullStatusCodes.Contains(result.Error!.StatusCode)) return null;
        throw ToApiException(result.Error, operation);
    }

    /// <summary>Throw <see cref="ApiException"/> if the operation failed; otherwise no-op.</summary>
    private static void EnsureSuccess(ServiceResult result, string operation)
    {
        if (result.IsSuccess) return;
        throw ToApiException(result.Error!, operation);
    }

    /// <summary>Run DataAnnotations validation; throw <see cref="ApiException"/>(400, VALIDATION_ERROR) on failure.</summary>
    private static void Validate<T>(T dto, string operation) where T : notnull
    {
        var ctx = new ValidationContext(dto);
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(dto, ctx, results, validateAllProperties: true))
            return;

        var details = new Dictionary<string, string>();
        foreach (var r in results)
        {
            var key = r.MemberNames.FirstOrDefault() ?? "";
            details[key] = r.ErrorMessage ?? "Invalid value";
        }
        throw new ApiException(
            $"{operation} validation failed",
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR",
            details);
    }

    private static ApiException ToApiException(ServiceError error, string operation)
    {
        var statusCode = (HttpStatusCode)error.StatusCode;
        var message = string.IsNullOrEmpty(error.Message) ? $"{operation} failed" : error.Message;
        return new ApiException(message, statusCode, error.Code, error.Details);
    }

    private string BaseUrl() => (_appSettings.BaseUrl ?? "").TrimEnd('/');

    #endregion

    #region Dashboard

    public virtual async Task<DashboardDto?> GetDashboardAsync(CancellationToken ct = default)
    {
        var result = await dashboardService.GetDashboardAsync(ct);
        return Unwrap(result, "Get dashboard");
    }

    #endregion

    #region Collections

    public virtual async Task<List<CollectionResponseDto>> GetCollectionsAsync(CancellationToken ct = default)
    {
        var result = await collectionQueryService.GetRootCollectionsAsync(ct);
        return Unwrap(result, "Get collections");
    }

    public virtual async Task<CollectionResponseDto?> GetCollectionAsync(Guid id, CancellationToken ct = default)
    {
        var result = await collectionQueryService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get collection", 404);
    }

    public virtual async Task<CollectionResponseDto> CreateCollectionAsync(CreateCollectionDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create collection");
        var result = await collectionService.CreateAsync(dto, ct);
        return Unwrap(result, "Create collection");
    }

    public virtual async Task UpdateCollectionAsync(Guid id, UpdateCollectionDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update collection");
        var result = await collectionService.UpdateAsync(id, dto, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Update collection");
    }

    public virtual async Task DeleteCollectionAsync(Guid id, CancellationToken ct = default)
    {
        var result = await collectionService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete collection");
    }

    public virtual async Task<CollectionDeletionContextDto?> GetCollectionDeletionContextAsync(Guid id, CancellationToken ct = default)
    {
        var result = await collectionQueryService.GetDeletionContextAsync(id, ct);
        return UnwrapOrNullOn(result, "Get collection deletion context", 403, 404);
    }

    public virtual async Task SetCollectionParentAsync(Guid collectionId, Guid? parentId, CancellationToken ct = default)
    {
        var result = await collectionService.SetParentAsync(collectionId, parentId, ct);
        EnsureSuccess(result, "Set collection parent");
    }

    public virtual async Task SetCollectionInheritParentAclAsync(Guid collectionId, bool inherit, CancellationToken ct = default)
    {
        var result = await collectionService.SetInheritParentAclAsync(collectionId, inherit, ct);
        EnsureSuccess(result, "Set collection inherit-acl");
    }

    public virtual async Task<int> CopyCollectionAclFromParentAsync(Guid collectionId, CancellationToken ct = default)
    {
        var result = await collectionService.CopyParentAclAsync(collectionId, ct);
        return Unwrap(result, "Copy collection ACL from parent");
    }

    public virtual async Task<List<CollectionAclResponseDto>> GetCollectionAclsAsync(Guid collectionId, CancellationToken ct = default)
    {
        var result = await collectionAclService.GetAclsAsync(collectionId, ct);
        return Unwrap(result, "Get collection ACLs").ToList();
    }

    public virtual async Task SetCollectionAccessAsync(Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default)
    {
        var result = await collectionAclService.SetAccessAsync(collectionId, principalType, principalId, role, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Set collection access");
    }

    public virtual async Task RevokeCollectionAccessAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default)
    {
        var result = await collectionAclService.RevokeAccessAsync(collectionId, principalType, principalId, ct);
        EnsureSuccess(result, "Revoke collection access");
    }

    public virtual async Task<List<UserSearchResultDto>> SearchUsersForAclAsync(Guid collectionId, string? query = null, CancellationToken ct = default)
    {
        var result = await collectionAclService.SearchUsersForAclAsync(collectionId, query, ct);
        return Unwrap(result, "Search users for ACL");
    }

    #endregion

    #region Assets

    public virtual async Task<AssetListResponse> GetAssetsAsync(
        Guid collectionId,
        string? query = null,
        string? type = null,
        string sortBy = Constants.SortBy.CreatedDesc,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var result = await assetQueryService.GetAssetsByCollectionAsync(collectionId, query, type, sortBy, skip, take, ct);
        return Unwrap(result, "Get assets");
    }

    public virtual async Task<AssetResponseDto?> GetAssetAsync(Guid id, CancellationToken ct = default)
    {
        var result = await assetQueryService.GetAssetAsync(id, ct);
        return UnwrapOrNullOn(result, "Get asset", 404);
    }

    public virtual async Task<AssetResponseDto> UpdateAssetAsync(Guid id, UpdateAssetDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update asset");
        var result = await assetService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update asset");
    }

    public virtual async Task<AssetUploadResult> UploadAssetAsync(
        Guid collectionId,
        string title,
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        // Direct upload path through IAssetUploadService — replaces the multipart
        // form post the legacy HttpClient client used. fileSize defaults to -1
        // when unknown; the service prefers it but tolerates -1.
        long fileSize = -1;
        try { if (fileStream.CanSeek) fileSize = fileStream.Length; } catch { /* not seekable */ }

        var result = await assetUploadService.UploadAsync(
            fileStream, fileName, contentType, fileSize, collectionId, title,
            skipDuplicateCheck: false, ct);
        return Unwrap(result, "Upload asset");
    }

    public virtual async Task<InitUploadResponse> InitUploadAsync(
        Guid? collectionId,
        string fileName,
        string contentType,
        long fileSize,
        string? title = null,
        CancellationToken ct = default)
    {
        var request = new InitUploadRequest
        {
            CollectionId = collectionId,
            FileName = fileName,
            ContentType = contentType,
            FileSize = fileSize,
            Title = title ?? Path.GetFileNameWithoutExtension(fileName)
        };
        Validate(request, "Init upload");
        var result = await assetUploadService.InitUploadAsync(request, ct);
        return Unwrap(result, "Init upload");
    }

    public virtual async Task<AssetUploadResult> ConfirmUploadAsync(Guid assetId, bool force = false, CancellationToken ct = default)
    {
        var result = await assetUploadService.ConfirmUploadAsync(assetId, skipDuplicateCheck: force, ct);
        return Unwrap(result, "Confirm upload");
    }

    public virtual async Task<InitUploadResponse> SaveImageCopyAsync(
        Guid sourceAssetId, string contentType, long fileSize, string? title = null, Guid? collectionId = null, CancellationToken ct = default)
    {
        var request = new SaveImageCopyRequest
        {
            ContentType = contentType,
            FileSize = fileSize,
            Title = title,
            CollectionId = collectionId
        };
        Validate(request, "Save image copy");
        var result = await assetUploadService.SaveImageCopyAsync(sourceAssetId, request, ct);
        return Unwrap(result, "Save image copy");
    }

    public virtual async Task<InitUploadResponse> ReplaceImageFileAsync(
        Guid assetId, string contentType, long fileSize, CancellationToken ct = default)
    {
        var request = new ReplaceImageFileRequest
        {
            ContentType = contentType,
            FileSize = fileSize
        };
        Validate(request, "Replace image file");
        var result = await assetUploadService.ReplaceImageFileAsync(assetId, request, ct);
        return Unwrap(result, "Replace image file");
    }

    /// <summary>
    /// Optional parameters for <see cref="ApplyEditAsync"/>.
    /// </summary>
    public record ImageEditOptions(
        string? Title = null,
        string? EditDocument = null,
        Guid? DestinationCollectionId = null,
        Guid[]? PresetIds = null);

    public virtual async Task<ImageEditResultDto> ApplyEditAsync(
        Guid assetId, Stream renderedPng, string fileName, ImageEditSaveMode saveMode,
        ImageEditOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ImageEditOptions();

        var dto = new ImageEditRequestDto
        {
            SaveMode = saveMode,
            PresetIds = options.PresetIds,
            Title = options.Title,
            EditDocument = options.EditDocument,
            DestinationCollectionId = options.DestinationCollectionId
        };
        Validate(dto, "Apply image edit");

        long fileSize = -1;
        try { if (renderedPng.CanSeek) fileSize = renderedPng.Length; } catch { /* not seekable */ }

        var result = await imageEditingService.ApplyEditAsync(assetId, dto, renderedPng, fileName, fileSize, ct);
        return Unwrap(result, "Apply image edit");
    }

    public virtual async Task DeleteAssetAsync(Guid id, Guid? fromCollectionId = null, CancellationToken ct = default)
    {
        var result = await assetService.DeleteAsync(id, fromCollectionId, ct);
        EnsureSuccess(result, "Delete asset");
    }

    public virtual async Task<BulkDeleteAssetsResponse> BulkDeleteAssetsAsync(
        List<Guid> assetIds, Guid? fromCollectionId = null, CancellationToken ct = default)
    {
        var request = new BulkDeleteAssetsRequest { AssetIds = assetIds, FromCollectionId = fromCollectionId };
        Validate(request, "Bulk delete assets");
        var result = await assetService.BulkDeleteAsync(request, ct);
        return Unwrap(result, "Bulk delete assets");
    }

    public virtual async Task<AssetDeletionContextDto> GetAssetDeletionContextAsync(Guid id, CancellationToken ct = default)
    {
        var result = await assetQueryService.GetDeletionContextAsync(id, ct);
        return Unwrap(result, "Get asset deletion context");
    }

    #endregion

    #region Shares

    public virtual async Task<ShareResponseDto> CreateShareAsync(
        Guid scopeId,
        string scopeType,
        DateTime? expiresAt = null,
        string? password = null,
        List<string>? notifyEmails = null,
        CancellationToken ct = default)
    {
        var dto = new CreateShareDto
        {
            ScopeId = scopeId,
            ScopeType = scopeType,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            Password = password,
            NotifyEmails = notifyEmails
        };
        Validate(dto, "Create share");
        var result = await authShareAccessService.CreateShareAsync(dto, BaseUrl(), ct);
        return Unwrap(result, "Create share");
    }

    public virtual async Task UpdateSharePasswordAsync(Guid shareId, string newPassword, CancellationToken ct = default)
    {
        var dto = new UpdateSharePasswordDto { Password = newPassword };
        Validate(dto, "Update share password");
        var result = await authShareAccessService.UpdateSharePasswordAsync(shareId, newPassword, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Update share password");
    }

    public virtual async Task<string> GetShareTokenAsync(Guid shareId, CancellationToken ct = default)
    {
        var result = await shareAdminService.GetShareTokenAsync(shareId, ct);
        if (!result.IsSuccess && result.Error!.StatusCode == 404) return string.Empty;
        return Unwrap(result, "Get share token").Token ?? string.Empty;
    }

    public virtual async Task<string?> GetSharePasswordAsync(Guid shareId, CancellationToken ct = default)
    {
        var result = await shareAdminService.GetSharePasswordAsync(shareId, ct);
        if (!result.IsSuccess && result.Error!.StatusCode == 404) return null;
        return Unwrap(result, "Get share password").Password;
    }

    public virtual async Task RevokeShareAsync(Guid id, CancellationToken ct = default)
    {
        var result = await authShareAccessService.RevokeShareAsync(id, ct);
        EnsureSuccess(result, "Revoke share");
    }

    /// <summary>
    /// Gets shared content by token. Returns the content DTO on success, or throws
    /// <see cref="ApiException"/> on failure (callers should inspect <c>ErrorCode</c>
    /// for "PASSWORD_REQUIRED", <see cref="Constants.ShareErrorCodes.Revoked"/>, etc.).
    /// </summary>
    public virtual async Task<ISharedContentDto> GetSharedContentAsync(
        string token, string? password = null, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await publicShareAccessService.GetSharedContentAsync(token, password, skip, take, ct);
        return Unwrap(result, "Get shared content");
    }

    /// <summary>
    /// Requests a short-lived access token for a password-protected share. Returns
    /// null on any error (preserves the legacy nullable-on-error contract).
    /// </summary>
    public virtual async Task<ShareAccessTokenResponse?> GetShareAccessTokenAsync(
        string token, string password, CancellationToken ct = default)
    {
        var result = await publicShareAccessService.CreateAccessTokenAsync(token, password, ct);
        return result.IsSuccess ? result.Value : null;
    }

    #endregion

    #region Presigned URLs

    public virtual Task<string> GetPresignedDownloadUrlAsync(Guid assetId, string objectKey, CancellationToken ct = default)
    {
        // Browsers hit the rendition endpoint by URL; the service issues the
        // presigned 302 redirect at request time. Nothing to do here.
        return Task.FromResult($"/api/v1/assets/{assetId}/download");
    }

    #endregion

    #region Admin

    public virtual async Task<AdminSharesResponse> GetAllSharesAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await shareAdminService.GetAllSharesAsync(skip, take, ct);
        return Unwrap(result, "Get all shares");
    }

    public virtual async Task RevokeShareAdminAsync(Guid id, CancellationToken ct = default)
    {
        var result = await shareAdminService.AdminRevokeShareAsync(id, ct);
        EnsureSuccess(result, "Revoke share");
    }

    public virtual async Task DeleteShareAdminAsync(Guid id, CancellationToken ct = default)
    {
        var result = await shareAdminService.DeleteShareAsync(id, ct);
        EnsureSuccess(result, "Delete share");
    }

    public virtual async Task<int> BulkDeleteSharesByStatusAsync(string status, CancellationToken ct = default)
    {
        var result = await shareAdminService.BulkDeleteSharesByStatusAsync(status, ct);
        return Unwrap(result, $"Bulk delete {status} shares");
    }

    public virtual async Task<List<CollectionAccessDto>> GetCollectionAccessAsync(CancellationToken ct = default)
    {
        var result = await adminCollectionAclService.GetCollectionAccessTreeAsync(ct);
        return Unwrap(result, "Get collection access");
    }

    public virtual async Task AddCollectionAclAsync(
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

    public virtual async Task UpdateCollectionAclAsync(
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

    public virtual async Task RemoveCollectionAclAsync(Guid collectionId, string principalId, string principalType, CancellationToken ct = default)
    {
        var result = await adminCollectionAclService.AdminRevokeAccessAsync(collectionId, principalType, principalId, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Remove collection access");
    }

    public virtual async Task<BulkDeleteCollectionsResponse> BulkDeleteCollectionsAsync(List<Guid> collectionIds, bool deleteAssets = true, CancellationToken ct = default)
    {
        var result = await collectionAdminService.BulkDeleteAsync(collectionIds, deleteAssets, ct);
        return Unwrap(result, "Bulk delete collections");
    }

    public virtual async Task<BulkSetCollectionAccessResponse> BulkSetCollectionAccessAsync(
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

    public virtual async Task<List<UserAccessSummaryDto>> GetUsersAsync(CancellationToken ct = default)
    {
        var result = await userAdminQueryService.GetUsersAsync(ct);
        return Unwrap(result, "Get users");
    }

    public virtual async Task<List<KeycloakUserDto>> GetKeycloakUsersAsync(CancellationToken ct = default)
    {
        var result = await userAdminQueryService.GetKeycloakUsersAsync(ct);
        return Unwrap(result, "Get Keycloak users");
    }

    public virtual async Task<PaginatedKeycloakUsersResponse> GetKeycloakUsersPaginatedAsync(
        string? search = null, string? category = null,
        string? sortBy = null, bool sortDesc = false,
        int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await userAdminQueryService.GetKeycloakUsersPaginatedAsync(
            search, category, sortBy, sortDesc, skip, take, ct);
        return Unwrap(result, "Get Keycloak users (paginated)");
    }

    public virtual async Task<CreateUserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        Validate(request, "Create user");
        var result = await userAdminService.CreateUserAsync(request, BaseUrl(), ct);
        return Unwrap(result, "Create user");
    }

    public virtual async Task SendPasswordResetEmailAsync(string userId, CancellationToken ct = default)
    {
        var result = await userAdminService.SendPasswordResetEmailAsync(userId, ct);
        EnsureSuccess(result, "Send password reset email");
    }

    public virtual async Task<DeleteUserResponse> DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        var result = await userAdminService.DeleteUserAsync(userId, ct);
        return Unwrap(result, "Delete user");
    }

    public virtual async Task<UserSyncResult> SyncDeletedUsersAsync(bool dryRun = false, CancellationToken ct = default)
    {
        var result = await userAdminService.SyncDeletedUsersAsync(dryRun, ct);
        return Unwrap(result, "Sync deleted users");
    }

    public virtual async Task<List<AuditEventDto>> GetAuditEventsAsync(int take = 200, CancellationToken ct = default)
    {
        var result = await auditQueryService.GetRecentAuditEventsAsync(take, ct);
        return Unwrap(result, "Get audit events");
    }

    public virtual async Task<AuditQueryResponse> GetAuditEventsPaginatedAsync(
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

    public virtual async Task<List<ExportPresetDto>> GetExportPresetsAsync(CancellationToken ct = default)
    {
        var result = await exportPresetQueryService.GetAllAsync(ct);
        return Unwrap(result, "Get export presets");
    }

    public virtual async Task<ExportPresetDto?> GetExportPresetAsync(Guid id, CancellationToken ct = default)
    {
        var result = await exportPresetQueryService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get export preset", 404);
    }

    public virtual async Task<ExportPresetDto> CreateExportPresetAsync(CreateExportPresetDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create export preset");
        var result = await exportPresetService.CreateAsync(dto, ct);
        return Unwrap(result, "Create export preset");
    }

    public virtual async Task UpdateExportPresetAsync(Guid id, UpdateExportPresetDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update export preset");
        var result = await exportPresetService.UpdateAsync(id, dto, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Update export preset");
    }

    public virtual async Task DeleteExportPresetAsync(Guid id, CancellationToken ct = default)
    {
        var result = await exportPresetService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete export preset");
    }

    #endregion

    #region Personal Access Tokens

    public virtual async Task<List<PersonalAccessTokenDto>> GetMyPersonalAccessTokensAsync(CancellationToken ct = default)
    {
        var result = await personalAccessTokenService.ListMineAsync(ct);
        return Unwrap(result, "List personal access tokens");
    }

    public virtual async Task<CreatedPersonalAccessTokenDto> CreatePersonalAccessTokenAsync(
        CreatePersonalAccessTokenRequest request,
        CancellationToken ct = default)
    {
        Validate(request, "Create personal access token");
        var result = await personalAccessTokenService.CreateAsync(request, ct);
        return Unwrap(result, "Create personal access token");
    }

    public virtual async Task RevokePersonalAccessTokenAsync(Guid id, CancellationToken ct = default)
    {
        var result = await personalAccessTokenService.RevokeAsync(id, ct);
        EnsureSuccess(result, "Revoke personal access token");
    }

    #endregion

    #region Notifications

    public virtual async Task<NotificationListResponse> GetNotificationsAsync(
        bool unreadOnly = false, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await notificationService.ListForCurrentUserAsync(unreadOnly, skip, take, ct);
        return Unwrap(result, "Get notifications");
    }

    public virtual async Task<int> GetNotificationUnreadCountAsync(CancellationToken ct = default)
    {
        var result = await notificationService.GetUnreadCountForCurrentUserAsync(ct);
        return Unwrap(result, "Get notification unread count").Count;
    }

    public virtual async Task MarkNotificationReadAsync(Guid id, CancellationToken ct = default)
    {
        var result = await notificationService.MarkReadAsync(id, ct);
        EnsureSuccess(result, "Mark notification read");
    }

    public virtual async Task<int> MarkAllNotificationsReadAsync(CancellationToken ct = default)
    {
        var result = await notificationService.MarkAllReadForCurrentUserAsync(ct);
        return Unwrap(result, "Mark all notifications read");
    }

    public virtual async Task DeleteNotificationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await notificationService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete notification");
    }

    public virtual async Task<NotificationPreferencesDto> GetNotificationPreferencesAsync(CancellationToken ct = default)
    {
        var result = await notificationPreferencesService.GetForCurrentUserAsync(ct);
        return Unwrap(result, "Get notification preferences");
    }

    public virtual async Task<NotificationPreferencesDto> UpdateNotificationPreferencesAsync(
        UpdateNotificationPreferencesDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update notification preferences");
        var result = await notificationPreferencesService.UpdateForCurrentUserAsync(dto, ct);
        return Unwrap(result, "Update notification preferences");
    }

    #endregion

    #region Migrations (Admin)

    public virtual async Task<MigrationListResponse> GetMigrationsAsync(int skip = 0, int take = 20, CancellationToken ct = default)
    {
        var result = await migrationService.ListAsync(skip, take, ct);
        return Unwrap(result, "Get migrations");
    }

    public virtual async Task<MigrationResponseDto> GetMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.GetByIdAsync(id, ct);
        return Unwrap(result, "Get migration");
    }

    public virtual async Task<MigrationResponseDto> CreateMigrationAsync(CreateMigrationDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create migration");
        var result = await migrationService.CreateAsync(dto, ct);
        return Unwrap(result, "Create migration");
    }

    public virtual async Task UploadMigrationManifestAsync(Guid id, Stream csvStream, string fileName, CancellationToken ct = default)
    {
        // fileName is part of the legacy multipart form; the service signature
        // is just (id, stream, ct).
        _ = fileName;
        var result = await migrationService.UploadManifestAsync(id, csvStream, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Upload migration manifest");
    }

    public virtual async Task UploadMigrationFilesAsync(Guid id, IEnumerable<(string FileName, Stream Stream, string ContentType)> files, CancellationToken ct = default)
    {
        var result = await migrationService.UploadStagingFilesAsync(id, files, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Upload migration files");
    }

    public virtual async Task StartMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.StartAsync(id, ct);
        EnsureSuccess(result, "Start migration");
    }

    public virtual async Task StartMigrationS3ScanAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.StartS3ScanAsync(id, ct);
        EnsureSuccess(result, "Start S3 scan");
    }

    public virtual async Task CancelMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.CancelAsync(id, ct);
        EnsureSuccess(result, "Cancel migration");
    }

    public virtual async Task RetryFailedMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.RetryFailedAsync(id, ct);
        EnsureSuccess(result, "Retry failed migration items");
    }

    public virtual async Task<MigrationProgressDto> GetMigrationProgressAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.GetProgressAsync(id, ct);
        return Unwrap(result, "Get migration progress");
    }

    public virtual async Task<MigrationItemListResponse> GetMigrationItemsAsync(Guid id, string? statusFilter = null, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await migrationService.GetItemsAsync(id, statusFilter, skip, take, ct);
        return Unwrap(result, "Get migration items");
    }

    public virtual async Task DeleteMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete migration");
    }

    /// <summary>
    /// Generates the migration outcome CSV in-process. The legacy HTTP endpoint
    /// produced the same shape (header + per-item rows); this mirrors it so
    /// callers receive an identical stream.
    /// </summary>
    public virtual async Task<Stream> DownloadMigrationOutcomeAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.GetItemsAsync(id, null, 0, 100_000, ct);
        var items = Unwrap(result, "Download migration outcome").Items;

        var csv = new StringBuilder();
        csv.AppendLine("external_id,filename,status,target_asset_id,error_code,error_message");
        foreach (var item in items)
        {
            csv.Append(EscapeCsvField(item.ExternalId ?? "")).Append(',');
            csv.Append(EscapeCsvField(item.FileName)).Append(',');
            csv.Append(EscapeCsvField(item.Status)).Append(',');
            csv.Append(item.AssetId?.ToString() ?? "").Append(',');
            csv.Append(EscapeCsvField(item.ErrorCode ?? "")).Append(',');
            csv.Append(EscapeCsvField(item.ErrorMessage ?? ""));
            csv.AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return new MemoryStream(bytes, writable: false);
    }

    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuoting) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public virtual async Task UnstageMigrationItemAsync(Guid migrationId, Guid itemId, CancellationToken ct = default)
    {
        var result = await migrationService.UnstageMigrationItemAsync(migrationId, itemId, ct);
        EnsureSuccess(result, "Unstage migration item");
    }

    public virtual async Task<int> BulkDeleteMigrationsAsync(string filter, CancellationToken ct = default)
    {
        var result = await migrationService.BulkDeleteAsync(filter, ct);
        return Unwrap(result, "Bulk delete migrations");
    }

    #endregion

    #region Asset Collections (Multi-Collection)

    public virtual async Task<List<AssetCollectionDto>> GetAssetCollectionsAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetQueryService.GetAssetCollectionsAsync(assetId, ct);
        return Unwrap(result, "Get asset collections").ToList();
    }

    public virtual async Task<List<AssetDerivativeDto>> GetAssetDerivativesAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetQueryService.GetDerivativesAsync(assetId, ct);
        return Unwrap(result, "Get asset derivatives");
    }

    public virtual async Task AddAssetToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var result = await assetService.AddToCollectionAsync(assetId, collectionId, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Add asset to collection");
    }

    public virtual async Task RemoveAssetFromCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var result = await assetService.RemoveFromCollectionAsync(assetId, collectionId, ct);
        EnsureSuccess(result, "Remove asset from collection");
    }

    #endregion

    #region Metadata Schemas

    public virtual async Task<List<MetadataSchemaDto>> GetMetadataSchemasAsync(CancellationToken ct = default)
    {
        var result = await metadataSchemaQueryService.GetAllAsync(ct);
        return Unwrap(result, "Get metadata schemas");
    }

    public virtual async Task<MetadataSchemaDto?> GetMetadataSchemaAsync(Guid id, CancellationToken ct = default)
    {
        var result = await metadataSchemaQueryService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get metadata schema", 404);
    }

    public virtual async Task<List<MetadataSchemaDto>> GetApplicableMetadataSchemasAsync(string? assetType = null, Guid? collectionId = null, CancellationToken ct = default)
    {
        var result = await metadataSchemaQueryService.GetApplicableAsync(assetType, collectionId, ct);
        return Unwrap(result, "Get applicable metadata schemas");
    }

    public virtual async Task<MetadataSchemaDto> CreateMetadataSchemaAsync(CreateMetadataSchemaDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create metadata schema");
        var result = await metadataSchemaService.CreateAsync(dto, ct);
        return Unwrap(result, "Create metadata schema");
    }

    public virtual async Task<MetadataSchemaDto> UpdateMetadataSchemaAsync(Guid id, UpdateMetadataSchemaDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update metadata schema");
        var result = await metadataSchemaService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update metadata schema");
    }

    public virtual async Task DeleteMetadataSchemaAsync(Guid id, bool force = false, CancellationToken ct = default)
    {
        var result = await metadataSchemaService.DeleteAsync(id, force, ct);
        EnsureSuccess(result, "Delete metadata schema");
    }

    #endregion

    #region Taxonomies

    public virtual async Task<List<TaxonomySummaryDto>> GetTaxonomiesAsync(CancellationToken ct = default)
    {
        var result = await taxonomyQueryService.GetAllAsync(ct);
        return Unwrap(result, "Get taxonomies");
    }

    public virtual async Task<TaxonomyDto?> GetTaxonomyAsync(Guid id, CancellationToken ct = default)
    {
        var result = await taxonomyQueryService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get taxonomy", 404);
    }

    public virtual async Task<TaxonomyDto> CreateTaxonomyAsync(CreateTaxonomyDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create taxonomy");
        var result = await taxonomyService.CreateAsync(dto, ct);
        return Unwrap(result, "Create taxonomy");
    }

    public virtual async Task<TaxonomyDto> UpdateTaxonomyAsync(Guid id, UpdateTaxonomyDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update taxonomy");
        var result = await taxonomyService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update taxonomy");
    }

    public virtual async Task<TaxonomyDto> ReplaceTaxonomyTermsAsync(Guid id, List<UpsertTaxonomyTermDto> terms, CancellationToken ct = default)
    {
        var result = await taxonomyService.ReplaceTermsAsync(id, terms, ct);
        return Unwrap(result, "Replace taxonomy terms");
    }

    public virtual async Task DeleteTaxonomyAsync(Guid id, CancellationToken ct = default)
    {
        var result = await taxonomyService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete taxonomy");
    }

    #endregion

    #region Asset Metadata

    public virtual async Task<List<AssetMetadataValueDto>> GetAssetMetadataAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetMetadataService.GetByAssetIdAsync(assetId, ct);
        return Unwrap(result, "Get asset metadata");
    }

    public virtual async Task<List<AssetMetadataValueDto>> SetAssetMetadataAsync(Guid assetId, SetAssetMetadataDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Set asset metadata");
        var result = await assetMetadataService.SetAsync(assetId, dto, ct);
        return Unwrap(result, "Set asset metadata");
    }

    #endregion

    #region Asset Search

    public virtual async Task<AssetSearchResponse> SearchAssetsAsync(AssetSearchRequest request, CancellationToken ct = default)
    {
        Validate(request, "Search assets");
        var result = await assetSearchService.SearchAsync(request, ct);
        return Unwrap(result, "Search assets");
    }

    #endregion

    #region Saved Searches

    public virtual async Task<List<SavedSearchDto>> GetSavedSearchesAsync(CancellationToken ct = default)
    {
        var result = await savedSearchService.GetMineAsync(ct);
        return Unwrap(result, "Get saved searches");
    }

    public virtual async Task<SavedSearchDto?> GetSavedSearchAsync(Guid id, CancellationToken ct = default)
    {
        var result = await savedSearchService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get saved search", 404);
    }

    public virtual async Task<SavedSearchDto> CreateSavedSearchAsync(CreateSavedSearchDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create saved search");
        var result = await savedSearchService.CreateAsync(dto, ct);
        return Unwrap(result, "Create saved search");
    }

    public virtual async Task<SavedSearchDto> UpdateSavedSearchAsync(Guid id, UpdateSavedSearchDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update saved search");
        var result = await savedSearchService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update saved search");
    }

    public virtual async Task DeleteSavedSearchAsync(Guid id, CancellationToken ct = default)
    {
        var result = await savedSearchService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete saved search");
    }

    #endregion

    #region Admin Trash

    public virtual async Task<TrashListResponse> GetTrashAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await assetTrashService.GetAsync(skip, take, ct);
        return Unwrap(result, "Get trash");
    }

    public virtual async Task RestoreFromTrashAsync(Guid id, CancellationToken ct = default)
    {
        var result = await assetTrashService.RestoreAsync(id, ct);
        EnsureSuccess(result, "Restore from trash");
    }

    public virtual async Task PurgeFromTrashAsync(Guid id, CancellationToken ct = default)
    {
        var result = await assetTrashService.PurgeAsync(id, ct);
        EnsureSuccess(result, "Purge from trash");
    }

    public virtual async Task<EmptyTrashResponse> EmptyTrashAsync(CancellationToken ct = default)
    {
        var result = await assetTrashService.EmptyAsync(ct);
        return Unwrap(result, "Empty trash");
    }

    #endregion

    #region Asset Versions

    public virtual async Task<List<AssetVersionDto>> GetAssetVersionsAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetVersionService.GetForAssetAsync(assetId, ct);
        return Unwrap(result, "Get asset versions");
    }

    public virtual async Task<AssetVersionDto> RestoreAssetVersionAsync(Guid assetId, int versionNumber, CancellationToken ct = default)
    {
        var result = await assetVersionService.RestoreAsync(assetId, versionNumber, ct);
        return Unwrap(result, "Restore asset version");
    }

    public virtual async Task PruneAssetVersionAsync(Guid assetId, int versionNumber, CancellationToken ct = default)
    {
        var result = await assetVersionService.PruneAsync(assetId, versionNumber, ct);
        EnsureSuccess(result, "Prune asset version");
    }

    #endregion

    #region Asset Comments

    public virtual async Task<List<AssetCommentResponseDto>> GetAssetCommentsAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetCommentService.ListForAssetAsync(assetId, ct);
        return Unwrap(result, "Get asset comments");
    }

    public virtual async Task<AssetCommentResponseDto> CreateAssetCommentAsync(
        Guid assetId, CreateAssetCommentDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create asset comment");
        var result = await assetCommentService.CreateAsync(assetId, dto, ct);
        return Unwrap(result, "Create asset comment");
    }

    public virtual async Task<AssetCommentResponseDto> UpdateAssetCommentAsync(
        Guid assetId, Guid commentId, UpdateAssetCommentDto dto, CancellationToken ct = default)
    {
        // assetId kept for caller-side context; the service identifies the comment
        // by its own primary key and re-checks the asset relationship internally.
        _ = assetId;
        Validate(dto, "Update asset comment");
        var result = await assetCommentService.UpdateAsync(commentId, dto, ct);
        return Unwrap(result, "Update asset comment");
    }

    public virtual async Task DeleteAssetCommentAsync(Guid assetId, Guid commentId, CancellationToken ct = default)
    {
        _ = assetId;
        var result = await assetCommentService.DeleteAsync(commentId, ct);
        EnsureSuccess(result, "Delete asset comment");
    }

    #endregion

    #region Asset Workflow (T3-WF-01)

    public virtual async Task<AssetWorkflowResponseDto> GetAssetWorkflowAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.GetAsync(assetId, ct);
        return Unwrap(result, "Get asset workflow");
    }

    public virtual async Task<AssetWorkflowResponseDto> SubmitAssetForReviewAsync(Guid assetId, string? reason, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.SubmitAsync(assetId, new WorkflowActionDto { Reason = reason }, ct);
        return Unwrap(result, "submit asset workflow");
    }

    public virtual async Task<AssetWorkflowResponseDto> ApproveAssetAsync(Guid assetId, string? reason, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.ApproveAsync(assetId, new WorkflowActionDto { Reason = reason }, ct);
        return Unwrap(result, "approve asset workflow");
    }

    public virtual async Task<AssetWorkflowResponseDto> RejectAssetAsync(Guid assetId, string reason, CancellationToken ct = default)
    {
        var dto = new WorkflowRejectDto { Reason = reason };
        Validate(dto, "Reject asset");
        var result = await assetWorkflowService.RejectAsync(assetId, dto, ct);
        return Unwrap(result, "reject asset workflow");
    }

    public virtual async Task<AssetWorkflowResponseDto> PublishAssetAsync(Guid assetId, string? reason, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.PublishAsync(assetId, new WorkflowActionDto { Reason = reason }, ct);
        return Unwrap(result, "publish asset workflow");
    }

    public virtual async Task<AssetWorkflowResponseDto> UnpublishAssetAsync(Guid assetId, string? reason, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.UnpublishAsync(assetId, new WorkflowActionDto { Reason = reason }, ct);
        return Unwrap(result, "unpublish asset workflow");
    }

    #endregion

    #region Webhooks (T3-INT-01)

    public virtual async Task<List<WebhookResponseDto>> GetWebhooksAsync(CancellationToken ct = default)
    {
        var result = await webhookService.ListAsync(ct);
        return Unwrap(result, "Get webhooks");
    }

    public virtual async Task<CreatedWebhookDto> CreateWebhookAsync(CreateWebhookDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create webhook");
        var result = await webhookService.CreateAsync(dto, ct);
        return Unwrap(result, "Create webhook");
    }

    public virtual async Task<WebhookResponseDto> UpdateWebhookAsync(Guid id, UpdateWebhookDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update webhook");
        var result = await webhookService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update webhook");
    }

    public virtual async Task DeleteWebhookAsync(Guid id, CancellationToken ct = default)
    {
        var result = await webhookService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete webhook");
    }

    public virtual async Task<CreatedWebhookDto> RotateWebhookSecretAsync(Guid id, CancellationToken ct = default)
    {
        var result = await webhookService.RotateSecretAsync(id, ct);
        return Unwrap(result, "Rotate webhook secret");
    }

    public virtual async Task<WebhookDeliveryResponseDto> SendWebhookTestAsync(Guid id, CancellationToken ct = default)
    {
        var result = await webhookService.SendTestAsync(id, ct);
        return Unwrap(result, "Send webhook test");
    }

    public virtual async Task<List<WebhookDeliveryResponseDto>> GetWebhookDeliveriesAsync(
        Guid id, int take = 50, CancellationToken ct = default)
    {
        var result = await webhookService.ListDeliveriesAsync(id, take, ct);
        return Unwrap(result, "Get webhook deliveries");
    }

    #endregion

    #region Brands (T4-BP-01)

    public virtual async Task<List<BrandResponseDto>> GetBrandsAsync(CancellationToken ct = default)
    {
        var result = await brandService.ListAsync(ct);
        return Unwrap(result, "Get brands");
    }

    public virtual async Task<BrandResponseDto> CreateBrandAsync(CreateBrandDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create brand");
        var result = await brandService.CreateAsync(dto, ct);
        return Unwrap(result, "Create brand");
    }

    public virtual async Task<BrandResponseDto> UpdateBrandAsync(Guid id, UpdateBrandDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update brand");
        var result = await brandService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update brand");
    }

    public virtual async Task DeleteBrandAsync(Guid id, CancellationToken ct = default)
    {
        var result = await brandService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete brand");
    }

    public virtual async Task<BrandResponseDto> UploadBrandLogoAsync(
        Guid id, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var result = await brandService.UploadLogoAsync(id, content, fileName, contentType, ct);
        return Unwrap(result, "Upload brand logo");
    }

    public virtual async Task RemoveBrandLogoAsync(Guid id, CancellationToken ct = default)
    {
        var result = await brandService.RemoveLogoAsync(id, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Remove brand logo");
    }

    #endregion

    #region Guest invitations (T4-GUEST-01)

    public virtual async Task<List<GuestInvitationResponseDto>> GetGuestInvitationsAsync(CancellationToken ct = default)
    {
        var result = await guestInvitationService.ListAsync(ct);
        return Unwrap(result, "Get guest invitations");
    }

    public virtual async Task<CreatedGuestInvitationDto> CreateGuestInvitationAsync(
        CreateGuestInvitationDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create guest invitation");
        var result = await guestInvitationService.CreateAsync(dto, BaseUrl(), ct);
        return Unwrap(result, "Create guest invitation");
    }

    public virtual async Task RevokeGuestInvitationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await guestInvitationService.RevokeAsync(id, ct);
        EnsureSuccess(result, "Revoke guest invitation");
    }

    public virtual async Task<AcceptGuestInvitationResponseDto> AcceptGuestInvitationAsync(
        string token, CancellationToken ct = default)
    {
        var result = await guestInvitationService.AcceptAsync(token, ct);
        return Unwrap(result, "Accept guest invitation");
    }

    #endregion
}

/// <summary>
/// Exception thrown when a facade call fails. Carries the same shape callers
/// expected from the legacy HTTP-based client (status code, error code, details)
/// so error-handling code in pages does not need to change.
/// </summary>
public class ApiException : Exception
{
    /// <summary>The HTTP-equivalent status code (mapped from <see cref="ServiceError.StatusCode"/>).</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The structured error code (e.g. "DUPLICATE_ASSET", "NOT_FOUND", "PASSWORD_REQUIRED").</summary>
    public string? ErrorCode { get; }

    /// <summary>Additional structured details (e.g. validation field errors).</summary>
    public Dictionary<string, string>? Details { get; }

    public ApiException(string message, HttpStatusCode statusCode, string? errorCode = null, Dictionary<string, string>? details = null) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details;
    }

    public override string ToString()
    {
        return $"ApiException: {Message} (HTTP {(int)StatusCode}, Code={ErrorCode})";
    }
}
