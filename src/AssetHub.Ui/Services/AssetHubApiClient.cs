using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Watermarking;
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
///
/// The surface is large by design (one facade for the whole UI). It is split
/// across <c>AssetHubApiClient.&lt;Domain&gt;.cs</c> partial files, one per domain
/// seam; this file holds the constructor and the shared result-unwrapping helpers
/// every partial uses.
/// </summary>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for the UI's API surface — every domain service it routes to is one constructor parameter.")]
[SuppressMessage("Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "Single facade for the UI — every domain service it routes to counts as a coupled type.")]
public sealed partial class AssetHubApiClient(
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
    IAssetReviewQueryService assetReviewQueryService,
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
    IWatermarkService watermarkService,
    IWatermarkVerifier watermarkVerifier,
    IAnalyticsService analyticsService,
    IAnalyticsPdfJobService analyticsPdfJobService,
    IUserLookupService userLookupService,
    IOptions<AppSettings> appSettings) : IAssetHubApiClient
{
    private readonly AppSettings _appSettings = appSettings.Value;

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
}
