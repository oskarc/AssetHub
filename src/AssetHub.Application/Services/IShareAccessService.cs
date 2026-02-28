using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Public (anonymous) share operations: content retrieval, URL generation, and access tokens.
/// All methods accept a token and optional password; no authenticated user is required.
/// </summary>
public interface IPublicShareAccessService
{
    /// <summary>Get shared content (asset or collection) by token.</summary>
    Task<ServiceResult<ISharedContentDto>> GetSharedContentAsync(
        string token, string? password, int skip, int take, CancellationToken ct);

    /// <summary>Get a presigned download URL for a shared asset.</summary>
    Task<ServiceResult<string>> GetDownloadUrlAsync(
        string token, string? password, Guid? assetId, CancellationToken ct);

    /// <summary>Enqueue a ZIP build for all shared collection assets.</summary>
    Task<ServiceResult<ZipDownloadEnqueuedResponse>> EnqueueDownloadAllAsync(
        string token, string? password, CancellationToken ct);

    /// <summary>Get a presigned preview URL for a shared asset.</summary>
    Task<ServiceResult<string>> GetPreviewUrlAsync(
        string token, string? password, string? size, Guid? assetId, bool forceDownload,
        CancellationToken ct);

    /// <summary>
    /// Creates a short-lived access token that can be used in place of the password
    /// for query-string-based access (img/video/a elements). The caller must supply
    /// a valid password; the returned token is cryptographically signed and time-limited.
    /// </summary>
    Task<ServiceResult<ShareAccessTokenResponse>> CreateAccessTokenAsync(
        string token, string? password, CancellationToken ct);
}

/// <summary>
/// Authenticated share management operations: create, revoke, and update password.
/// All methods require an authenticated user in the current scope.
/// </summary>
public interface IAuthenticatedShareAccessService
{
    /// <summary>Create a new share link for an asset or collection.</summary>
    Task<ServiceResult<ShareResponseDto>> CreateShareAsync(
        CreateShareDto dto, string baseUrl, CancellationToken ct);

    /// <summary>Revoke a share (owner only).</summary>
    Task<ServiceResult> RevokeShareAsync(Guid shareId, CancellationToken ct);

    /// <summary>Update the password on an existing share (owner or admin).</summary>
    Task<ServiceResult<MessageResponse>> UpdateSharePasswordAsync(
        Guid shareId, string password, CancellationToken ct);
}
