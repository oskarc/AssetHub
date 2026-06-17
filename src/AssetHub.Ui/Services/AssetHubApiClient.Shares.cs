using AssetHub.Application;
using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<ShareResponseDto> CreateShareAsync(
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

    public async Task UpdateSharePasswordAsync(Guid shareId, string newPassword, CancellationToken ct = default)
    {
        var dto = new UpdateSharePasswordDto { Password = newPassword };
        Validate(dto, "Update share password");
        var result = await authShareAccessService.UpdateSharePasswordAsync(shareId, newPassword, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Update share password");
    }

    public async Task<string> GetShareTokenAsync(Guid shareId, CancellationToken ct = default)
    {
        var result = await shareAdminService.GetShareTokenAsync(shareId, ct);
        if (!result.IsSuccess && result.Error!.StatusCode == 404) return string.Empty;
        return Unwrap(result, "Get share token").Token ?? string.Empty;
    }

    public async Task<string?> GetSharePasswordAsync(Guid shareId, CancellationToken ct = default)
    {
        var result = await shareAdminService.GetSharePasswordAsync(shareId, ct);
        if (!result.IsSuccess && result.Error!.StatusCode == 404) return null;
        return Unwrap(result, "Get share password").Password;
    }

    public async Task RevokeShareAsync(Guid id, CancellationToken ct = default)
    {
        var result = await authShareAccessService.RevokeShareAsync(id, ct);
        EnsureSuccess(result, "Revoke share");
    }

    /// <summary>
    /// Gets shared content by token. Returns the content DTO on success, or throws
    /// <see cref="ApiException"/> on failure (callers should inspect <c>ErrorCode</c>
    /// for "PASSWORD_REQUIRED", <see cref="Constants.ShareErrorCodes.Revoked"/>, etc.).
    /// </summary>
    public async Task<ISharedContentDto> GetSharedContentAsync(
        string token, string? password = null, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await publicShareAccessService.GetSharedContentAsync(token, password, skip, take, ct);
        return Unwrap(result, "Get shared content");
    }

    /// <summary>
    /// Requests a short-lived access token for a password-protected share. Returns
    /// null on any error (preserves the legacy nullable-on-error contract).
    /// </summary>
    public async Task<ShareAccessTokenResponse?> GetShareAccessTokenAsync(
        string token, string password, CancellationToken ct = default)
    {
        var result = await publicShareAccessService.CreateAccessTokenAsync(token, password, ct);
        return result.IsSuccess ? result.Value : null;
    }
}
