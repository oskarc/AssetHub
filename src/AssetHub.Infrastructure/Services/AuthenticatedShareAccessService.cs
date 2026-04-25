using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Handles authenticated share management: create, revoke, and update password.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root: repo + auth + share service + audit + DataProtection + UnitOfWork + scoped CurrentUser. UnitOfWork added to wrap action+audit atomically (A-4).")]
public sealed class AuthenticatedShareAccessService(
    IShareRepository shareRepo,
    ICollectionAuthorizationService authService,
    IShareService shareService,
    IAuditService audit,
    IDataProtectionProvider dataProtection,
    IUnitOfWork uow,
    CurrentUser currentUser) : IAuthenticatedShareAccessService
{
    public async Task<ServiceResult<ShareResponseDto>> CreateShareAsync(
        CreateShareDto dto, string baseUrl, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return ServiceError.Forbidden("Authentication required to create shares");

        var validation = await shareService.ValidateScopeAsync(dto, ct);
        if (!validation.IsValid)
        {
            return validation.ErrorStatusCode == 404
                ? ServiceError.NotFound(validation.ErrorMessage!)
                : ServiceError.BadRequest(validation.ErrorMessage!);
        }

        // Authorization for sharing:
        // - Orphan assets: Only system admins (they're the only ones who can access the orphans page)
        // - Assets in collections / Collections: Require Manager role on at least one collection
        var hasAccess = false;
        if (validation.IsOrphanAsset)
        {
            hasAccess = currentUser.IsSystemAdmin;
        }
        else
        {
            foreach (var collectionId in validation.CollectionIdsToCheck)
            {
                if (await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Manager, ct))
                {
                    hasAccess = true;
                    break;
                }
            }
        }
        if (!hasAccess)
            return ServiceError.Forbidden("You don't have permission to share this resource");

        var result = await shareService.CreateShareAsync(dto, userId, baseUrl, ct);
        if (result.IsError)
            return ServiceError.BadRequest(result.ErrorMessage!);
        return result.Response!;
    }

    public async Task<ServiceResult> RevokeShareAsync(Guid shareId, CancellationToken ct)
    {
        var share = await shareRepo.GetByIdAsync(shareId, ct);
        if (share is null)
            return ServiceError.NotFound("Share not found");

        var userId = currentUser.UserId;
        if (share.CreatedByUserId != userId)
            return ServiceError.Forbidden("You don't have permission to revoke this share");

        share.RevokedAt = DateTime.UtcNow;

        // Revoke + audit atomic (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await shareRepo.UpdateAsync(share, tct);
            await audit.LogAsync("share.revoked", Constants.ScopeTypes.Share, shareId, userId,
                new() { ["scopeType"] = share.ScopeType, ["scopeId"] = share.ScopeId },
                tct);
        }, ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<MessageResponse>> UpdateSharePasswordAsync(
        Guid shareId, string password, CancellationToken ct)
    {
        var share = await shareRepo.GetByIdAsync(shareId, ct);
        if (share is null)
            return ServiceError.NotFound("Share not found");

        var userId = currentUser.UserId;
        var isAdmin = currentUser.IsSystemAdmin;
        if (share.CreatedByUserId != userId && !isAdmin)
            return ServiceError.Forbidden("You don't have permission to update this share");

        if (string.IsNullOrWhiteSpace(password))
            return ServiceError.BadRequest("Password cannot be empty");
        var pwError = InputValidation.ValidateSharePassword(password);
        if (pwError is not null)
            return ServiceError.BadRequest(pwError);

        share.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        // Bump the version so all access tokens issued under the previous
        // password are invalidated (P-3 / security review).
        share.PasswordVersion++;

        // Store encrypted password so admins can retrieve it later
        var protector = dataProtection.CreateProtector(Constants.DataProtection.SharePasswordProtector);
        var protectedBytes = protector.Protect(System.Text.Encoding.UTF8.GetBytes(password));
        share.PasswordEncrypted = Convert.ToBase64String(protectedBytes);

        // Update + audit atomic (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await shareRepo.UpdateAsync(share, tct);
            await audit.LogAsync("share.password_updated", Constants.ScopeTypes.Share, shareId, userId,
                new() { ["scopeType"] = share.ScopeType, ["scopeId"] = share.ScopeId },
                tct);
        }, ct);

        return new MessageResponse("Password updated successfully");
    }
}
