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
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for admin share ops: repos + user lookup + audit + DataProtection + UnitOfWork + scoped CurrentUser + logger. UnitOfWork added to wrap action+audit atomically (A-4).")]
public sealed class ShareAdminService(
    IShareRepository shareRepo,
    ICollectionRepository collectionRepo,
    IUserLookupService userLookup,
    IAuditService audit,
    IDataProtectionProvider dataProtection,
    IUnitOfWork uow,
    CurrentUser currentUser,
    ILogger<ShareAdminService> logger) : IShareAdminService
{
    private const string ShareNotFound = "Share not found";

    public async Task<ServiceResult<AdminSharesResponse>> GetAllSharesAsync(int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, Constants.Limits.AdminShareQueryLimit);
        var total = await shareRepo.CountAllAsync(ct);
        var shares = await shareRepo.GetAllAsync(new ShareQueryOptions(IncludeAsset: true, IncludeCollection: true, Skip: skip, Take: take), ct);
        var userIds = shares.Select(s => s.CreatedByUserId).Distinct().ToList();
        var userNames = await userLookup.GetUserNamesAsync(userIds, ct);

        // Load collection memberships for asset-type shares
        var assetShares = shares.Where(s => s.ScopeType == ShareScopeType.Asset && s.Asset is not null).ToList();
        var assetCollectionMap = new Dictionary<Guid, List<string>>();
        if (assetShares.Count > 0)
        {
            var assetIds = assetShares.Select(s => s.ScopeId).Distinct().ToList();
            assetCollectionMap = await collectionRepo.GetCollectionNamesForAssetsAsync(assetIds, ct);
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
        var share = await shareRepo.GetByIdAsync(shareId, ct);
        if (share is null)
            return ServiceError.NotFound(ShareNotFound);

        if (string.IsNullOrEmpty(share.TokenEncrypted))
            return ServiceError.NotFound("Share token not available — this share was created before token encryption was enabled");

        try
        {
            var protector = dataProtection.CreateProtector(Constants.DataProtection.ShareTokenProtector);
            var protectedBytes = Convert.FromBase64String(share.TokenEncrypted);
            var token = System.Text.Encoding.UTF8.GetString(protector.Unprotect(protectedBytes));
            return new ShareTokenResponse { Token = token };
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            logger.LogError(ex, "Failed to decrypt share token for share {ShareId}. Data Protection keys may have been rotated.", shareId);
            return ServiceError.Server("Unable to decrypt share token — encryption keys may have changed");
        }
        catch (FormatException ex)
        {
            logger.LogError(ex, "Corrupted TokenEncrypted data for share {ShareId}", shareId);
            return ServiceError.Server("Share token data is corrupted");
        }
    }

    public async Task<ServiceResult<SharePasswordResponse>> GetSharePasswordAsync(Guid shareId, CancellationToken ct)
    {
        var share = await shareRepo.GetByIdAsync(shareId, ct);
        if (share is null)
            return ServiceError.NotFound(ShareNotFound);

        if (string.IsNullOrEmpty(share.PasswordEncrypted))
            return ServiceError.NotFound("Share password not available — this share was created before password encryption was enabled, or has no password set");

        try
        {
            var protector = dataProtection.CreateProtector(Constants.DataProtection.SharePasswordProtector);
            var protectedBytes = Convert.FromBase64String(share.PasswordEncrypted);
            var password = System.Text.Encoding.UTF8.GetString(protector.Unprotect(protectedBytes));
            return new SharePasswordResponse { Password = password };
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            logger.LogError(ex, "Failed to decrypt share password for share {ShareId}. Data Protection keys may have been rotated.", shareId);
            return ServiceError.Server("Unable to decrypt share password — encryption keys may have changed");
        }
        catch (FormatException ex)
        {
            logger.LogError(ex, "Corrupted PasswordEncrypted data for share {ShareId}", shareId);
            return ServiceError.Server("Share password data is corrupted");
        }
    }

    public async Task<ServiceResult> AdminRevokeShareAsync(Guid shareId, CancellationToken ct)
    {
        var share = await shareRepo.GetByIdAsync(shareId, ct);
        if (share is null)
            return ServiceError.NotFound(ShareNotFound);

        if (share.RevokedAt.HasValue)
            return ServiceError.BadRequest("Share is already revoked");

        share.RevokedAt = DateTime.UtcNow;

        // Revoke + audit atomic (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await shareRepo.UpdateAsync(share, tct);
            await audit.LogAsync("share.revoked", Constants.ScopeTypes.Share, shareId, currentUser.UserId,
                new() { ["admin"] = true }, tct);
        }, ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult> DeleteShareAsync(Guid shareId, CancellationToken ct)
    {
        var share = await shareRepo.GetByIdAsync(shareId, ct);
        if (share is null)
            return ServiceError.NotFound(ShareNotFound);

        if (share.RevokedAt is null && share.ExpiresAt > DateTime.UtcNow)
            return ServiceError.BadRequest("Cannot delete an active share — revoke it first");

        // Delete + audit atomic (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await shareRepo.DeleteAsync(shareId, tct);
            await audit.LogAsync("share.deleted", Constants.ScopeTypes.Share, shareId, currentUser.UserId,
                new() { ["admin"] = true }, tct);
        }, ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<int>> BulkDeleteSharesByStatusAsync(string status, CancellationToken ct)
    {
        bool isExpired = status.Equals(Constants.ShareStatus.Expired, StringComparison.OrdinalIgnoreCase);
        bool isRevoked = status.Equals(Constants.ShareStatus.Revoked, StringComparison.OrdinalIgnoreCase);
        if (!isExpired && !isRevoked)
            return ServiceError.BadRequest($"Invalid status: {status}. Must be 'expired' or 'revoked'.");

        // Bulk delete + audit atomic (A-4).
        var deleted = await uow.ExecuteAsync(async tct =>
        {
            var n = isExpired
                ? await shareRepo.DeleteExpiredAsync(tct)
                : await shareRepo.DeleteRevokedAsync(tct);
            await audit.LogAsync("shares.bulk_deleted", Constants.ScopeTypes.Share, Guid.Empty, currentUser.UserId,
                new() { ["status"] = status, ["count"] = n }, tct);
            return n;
        }, ct);

        return deleted;
    }
}
