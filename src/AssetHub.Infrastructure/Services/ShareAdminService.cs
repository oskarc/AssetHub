using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Admin share management: listing, token retrieval, and revocation.
/// </summary>
public class ShareAdminService : IShareAdminService
{
    private readonly IShareRepository _shareRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly IUserLookupService _userLookup;
    private readonly IAuditService _audit;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly CurrentUser _currentUser;
    private readonly ILogger<ShareAdminService> _logger;

    public ShareAdminService(
        IShareRepository shareRepo,
        ICollectionRepository collectionRepo,
        IUserLookupService userLookup,
        IAuditService audit,
        IDataProtectionProvider dataProtection,
        CurrentUser currentUser,
        ILogger<ShareAdminService> logger)
    {
        _shareRepo = shareRepo;
        _collectionRepo = collectionRepo;
        _userLookup = userLookup;
        _audit = audit;
        _dataProtection = dataProtection;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ServiceResult<AdminSharesResponse>> GetAllSharesAsync(int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, Constants.Limits.AdminShareQueryLimit);
        var total = await _shareRepo.CountAllAsync(ct);
        var shares = await _shareRepo.GetAllAsync(includeAsset: true, includeCollection: true, skip: skip, take: take, cancellationToken: ct);
        var userIds = shares.Select(s => s.CreatedByUserId).Distinct().ToList();
        var userNames = await _userLookup.GetUserNamesAsync(userIds, ct);

        // Load collection memberships for asset-type shares
        var assetShares = shares.Where(s => s.ScopeType == ShareScopeType.Asset && s.Asset != null).ToList();
        var assetCollectionMap = new Dictionary<Guid, List<string>>();
        if (assetShares.Count > 0)
        {
            var assetIds = assetShares.Select(s => s.ScopeId).Distinct().ToList();
            assetCollectionMap = await _collectionRepo.GetCollectionNamesForAssetsAsync(assetIds, ct);
        }

        var result = shares.Select(s => new AdminShareDto
        {
            Id = s.Id,
            ScopeType = s.ScopeType.ToDbString(),
            ScopeId = s.ScopeId,
            ScopeName = s.ScopeType == ShareScopeType.Asset
                ? s.Asset?.Title ?? "Unknown Asset"
                : s.Collection?.Name ?? "Unknown Collection",
            CreatedByUserId = s.CreatedByUserId,
            CreatedByUserName = userNames.TryGetValue(s.CreatedByUserId, out var name) ? name : $"Deleted User ({s.CreatedByUserId[..8]})",
            CreatedAt = s.CreatedAt,
            ExpiresAt = s.ExpiresAt,
            RevokedAt = s.RevokedAt,
            LastAccessedAt = s.LastAccessedAt,
            AccessCount = s.AccessCount,
            HasPassword = !string.IsNullOrEmpty(s.PasswordHash),
            Status = ShareHelpers.GetShareStatus(s.RevokedAt, s.ExpiresAt),
            CollectionNames = s.ScopeType == ShareScopeType.Asset && assetCollectionMap.TryGetValue(s.ScopeId, out var colNames)
                ? colNames
                : new List<string>()
        }).ToList();

        return new AdminSharesResponse { Total = total, Items = result };
    }

    public async Task<ServiceResult<ShareTokenResponse>> GetShareTokenAsync(Guid shareId, CancellationToken ct)
    {
        var share = await _shareRepo.GetByIdAsync(shareId, ct);
        if (share == null)
            return ServiceError.NotFound("Share not found");

        if (string.IsNullOrEmpty(share.TokenEncrypted))
            return ServiceError.NotFound("Share token not available — this share was created before token encryption was enabled");

        try
        {
            var protector = _dataProtection.CreateProtector(Constants.DataProtection.ShareTokenProtector);
            var protectedBytes = Convert.FromBase64String(share.TokenEncrypted);
            var token = System.Text.Encoding.UTF8.GetString(protector.Unprotect(protectedBytes));
            return new ShareTokenResponse { Token = token };
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to decrypt share token for share {ShareId}. Data Protection keys may have been rotated.", shareId);
            return ServiceError.Server("Unable to decrypt share token — encryption keys may have changed");
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Corrupted TokenEncrypted data for share {ShareId}", shareId);
            return ServiceError.Server("Share token data is corrupted");
        }
    }

    public async Task<ServiceResult<SharePasswordResponse>> GetSharePasswordAsync(Guid shareId, CancellationToken ct)
    {
        var share = await _shareRepo.GetByIdAsync(shareId, ct);
        if (share == null)
            return ServiceError.NotFound("Share not found");

        if (string.IsNullOrEmpty(share.PasswordEncrypted))
            return ServiceError.NotFound("Share password not available — this share was created before password encryption was enabled, or has no password set");

        try
        {
            var protector = _dataProtection.CreateProtector(Constants.DataProtection.SharePasswordProtector);
            var protectedBytes = Convert.FromBase64String(share.PasswordEncrypted);
            var password = System.Text.Encoding.UTF8.GetString(protector.Unprotect(protectedBytes));
            return new SharePasswordResponse { Password = password };
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to decrypt share password for share {ShareId}. Data Protection keys may have been rotated.", shareId);
            return ServiceError.Server("Unable to decrypt share password — encryption keys may have changed");
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Corrupted PasswordEncrypted data for share {ShareId}", shareId);
            return ServiceError.Server("Share password data is corrupted");
        }
    }

    public async Task<ServiceResult> AdminRevokeShareAsync(Guid shareId, CancellationToken ct)
    {
        var share = await _shareRepo.GetByIdAsync(shareId, ct);
        if (share == null)
            return ServiceError.NotFound("Share not found");

        if (share.RevokedAt.HasValue)
            return ServiceError.BadRequest("Share is already revoked");

        share.RevokedAt = DateTime.UtcNow;
        await _shareRepo.UpdateAsync(share, ct);

        await _audit.LogAsync("share.revoked", "share", shareId, _currentUser.UserId,
            new() { ["admin"] = true }, ct);

        return ServiceResult.Success;
    }
}
