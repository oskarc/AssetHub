using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dam.Infrastructure.Services;

/// <summary>
/// Orchestrates admin-only operations: share management, user lifecycle, and sync.
/// </summary>
public class AdminService : IAdminService
{
    private readonly IShareRepository _shareRepo;
    private readonly ICollectionAclRepository _aclRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly IUserLookupService _userLookup;
    private readonly IKeycloakUserService _keycloakUserService;
    private readonly IUserProvisioningService _provisioning;
    private readonly IUserCleanupService _cleanupService;
    private readonly IUserSyncService _syncService;
    private readonly IAuditService _audit;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IConfiguration _configuration;
    private readonly CurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        IShareRepository shareRepo,
        ICollectionAclRepository aclRepo,
        ICollectionRepository collectionRepo,
        IUserLookupService userLookup,
        IKeycloakUserService keycloakUserService,
        IUserProvisioningService provisioning,
        IUserCleanupService cleanupService,
        IUserSyncService syncService,
        IAuditService audit,
        IDataProtectionProvider dataProtection,
        IConfiguration configuration,
        CurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AdminService> logger)
    {
        _shareRepo = shareRepo;
        _aclRepo = aclRepo;
        _collectionRepo = collectionRepo;
        _userLookup = userLookup;
        _keycloakUserService = keycloakUserService;
        _provisioning = provisioning;
        _cleanupService = cleanupService;
        _syncService = syncService;
        _audit = audit;
        _dataProtection = dataProtection;
        _configuration = configuration;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    private HttpContext? HttpCtx => _httpContextAccessor.HttpContext;

    // ── Share Management ─────────────────────────────────────────────────────

    public async Task<ServiceResult<List<AdminShareDto>>> GetAllSharesAsync(CancellationToken ct)
    {
        var shares = await _shareRepo.GetAllAsync(includeAsset: true, includeCollection: true, ct);
        var userIds = shares.Select(s => s.CreatedByUserId).Distinct().ToList();
        var userNames = await _userLookup.GetUserNamesAsync(userIds, ct);

        var result = shares.Select(s => new AdminShareDto
        {
            Id = s.Id,
            ScopeType = s.ScopeType,
            ScopeId = s.ScopeId,
            ScopeName = s.ScopeType == Constants.ScopeTypes.Asset
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
            Status = ShareHelpers.GetShareStatus(s.RevokedAt, s.ExpiresAt)
        }).ToList();

        return result;
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

    public async Task<ServiceResult> AdminRevokeShareAsync(Guid shareId, CancellationToken ct)
    {
        var share = await _shareRepo.GetByIdAsync(shareId, ct);
        if (share == null)
            return ServiceError.NotFound("Share not found");

        if (share.RevokedAt.HasValue)
            return ServiceError.BadRequest("Share is already revoked");

        share.RevokedAt = DateTime.UtcNow;
        await _shareRepo.UpdateAsync(share, ct);

        var adminUserId = _currentUser.UserId;
        await _audit.LogAsync("share.revoked", "share", shareId, adminUserId,
            new() { ["admin"] = true }, HttpCtx, ct);

        return ServiceResult.Success;
    }

    // ── User Management ──────────────────────────────────────────────────────

    public async Task<ServiceResult<List<UserAccessSummaryDto>>> GetUsersAsync(CancellationToken ct)
    {
        var allAcls = await _aclRepo.GetAllAsync(ct);
        var allCollections = (await _collectionRepo.GetAllWithAclsAsync(ct)).ToDictionary(c => c.Id);

        var userIds = allAcls.Where(a => a.PrincipalType == "user").Select(a => a.PrincipalId).Distinct().ToList();
        var userNames = await _userLookup.GetUserNamesAsync(userIds, ct);

        var userAccess = allAcls
            .Where(a => a.PrincipalType == "user")
            .GroupBy(a => a.PrincipalId)
            .Select(g => new UserAccessSummaryDto
            {
                UserId = g.Key,
                UserName = userNames.TryGetValue(g.Key, out var name) ? name : $"Deleted User ({g.Key[..Math.Min(8, g.Key.Length)]})",
                CollectionCount = g.Count(),
                HighestRole = RoleHierarchy.GetHighestRole(g.Select(a => a.Role)),
                Collections = g.Select(a => new UserCollectionAccessDto
                {
                    CollectionId = a.CollectionId,
                    CollectionName = allCollections.TryGetValue(a.CollectionId, out var col) ? col.Name : "Unknown",
                    Role = a.Role
                }).ToList()
            })
            .OrderBy(u => u.UserName)
            .ToList();

        return userAccess;
    }

    public async Task<ServiceResult<List<KeycloakUserDto>>> GetKeycloakUsersAsync(CancellationToken ct)
    {
        var allUsers = await _userLookup.GetAllUsersAsync(ct);
        var allAcls = await _aclRepo.GetAllAsync(ct);

        var userAclGroups = allAcls
            .Where(a => a.PrincipalType == "user")
            .GroupBy(a => a.PrincipalId)
            .ToDictionary(g => g.Key, g => new
            {
                CollectionCount = g.Count(),
                HighestRole = RoleHierarchy.GetHighestRole(g.Select(a => a.Role))
            });

        var result = allUsers.Select(u =>
        {
            var hasAccess = userAclGroups.TryGetValue(u.Id, out var acl);
            return new KeycloakUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                CreatedAt = u.CreatedAt,
                CollectionCount = hasAccess ? acl!.CollectionCount : 0,
                HighestRole = hasAccess ? acl!.HighestRole : null
            };
        }).ToList();

        return result;
    }

    public async Task<ServiceResult<CreateUserResponse>> CreateUserAsync(
        CreateUserRequest request, string baseUrl, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        var firstName = request.FirstName?.Trim() ?? "";
        var lastName = request.LastName?.Trim() ?? "";

        if (!InputValidation.TryValidate(out var errors,
            ("username", InputValidation.ValidateUsername(username)),
            ("email", InputValidation.ValidateEmail(email)),
            ("firstName", InputValidation.ValidateRequired(firstName, "First name")),
            ("lastName", InputValidation.ValidateRequired(lastName, "Last name"))))
        {
            return ServiceError.Validation("Validation failed", errors);
        }

        // Server-side password generation — admin never sees/types the password
        var password = request.Password;
        if (string.IsNullOrWhiteSpace(password))
            password = Dam.Application.Helpers.PasswordGenerator.Generate(20);

        var collectionErrors = await _provisioning.ValidateCollectionsExistAsync(request.InitialCollectionIds, ct);
        if (collectionErrors.Count > 0)
            return ServiceError.Validation("One or more collections not found", collectionErrors);

        try
        {
            var userId = await _keycloakUserService.CreateUserAsync(
                username, email, firstName, lastName,
                password, true /* always temporary */, ct);

            _logger.LogInformation("Admin created user '{Username}' (ID: {UserId})", username, userId);

            var role = RoleHierarchy.ResolveRole(request.InitialRole);
            await _provisioning.GrantCollectionAccessAsync(request.InitialCollectionIds, userId, role, username, ct);

            // Send password setup email via Keycloak (user sets their own password)
            if (!string.IsNullOrWhiteSpace(email))
            {
                try
                {
                    await _keycloakUserService.SendExecuteActionsEmailAsync(
                        userId, new[] { "UPDATE_PASSWORD" }, lifespan: 86400, ct);
                    _logger.LogInformation(
                        "Sent password setup email to '{Email}' for new user '{Username}'",
                        email, username);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to send password setup email to '{Email}' for new user '{Username}'",
                        email, username);
                }
            }

            var adminUserId = _currentUser.UserId;
            await _audit.LogAsync("user.created", "user",
                Guid.TryParse(userId, out var uid) ? uid : null, adminUserId,
                new() { ["username"] = username, ["email"] = email },
                HttpCtx, ct);

            return new CreateUserResponse
            {
                UserId = userId,
                Username = username,
                Email = email,
                Message = request.InitialCollectionIds.Count > 0
                    ? $"User created and granted {role} access to {request.InitialCollectionIds.Count} collection(s)"
                    : "User created successfully"
            };
        }
        catch (KeycloakApiException ex)
        {
            _logger.LogWarning(ex, "Keycloak API error creating user '{Username}'", username);

            var adminUserId = _currentUser.UserId;
            await _audit.LogAsync("user.create_failed", "user", null, adminUserId,
                new() { ["username"] = username, ["error"] = ex.Message },
                HttpCtx, ct);

            return ex.StatusCode == 409
                ? ServiceError.Conflict(ex.Message)
                : ServiceError.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating user '{Username}'", username);
            return ServiceError.Server("An unexpected error occurred");
        }
    }

    public async Task<ServiceResult> SendPasswordResetEmailAsync(
        string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.BadRequest("User ID is required");

        try
        {
            await _keycloakUserService.SendExecuteActionsEmailAsync(
                userId, new[] { "UPDATE_PASSWORD" }, lifespan: 86400, ct);

            _logger.LogInformation("Admin sent password reset email for user '{UserId}'", userId);

            var adminUserId = _currentUser.UserId;
            await _audit.LogAsync("user.password_reset_email", "user",
                Guid.TryParse(userId, out var uid) ? uid : null, adminUserId,
                new() { ["targetUserId"] = userId },
                HttpCtx, ct);

            return ServiceResult.Success;
        }
        catch (KeycloakApiException ex)
        {
            _logger.LogWarning(ex, "Keycloak API error sending password reset email for user '{UserId}'", userId);
            return ex.StatusCode == 404
                ? ServiceError.NotFound(ex.Message)
                : ServiceError.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending password reset email for user '{UserId}'", userId);
            return ServiceError.Server("An unexpected error occurred");
        }
    }

    public async Task<ServiceResult<UserSyncResult>> SyncDeletedUsersAsync(bool dryRun, CancellationToken ct)
    {
        try
        {
            var result = await _syncService.SyncDeletedUsersAsync(dryRun, ct);

            if (!dryRun && result.DeletedUsers > 0)
            {
                var adminUserId = _currentUser.UserId;
                await _audit.LogAsync("user.sync.completed", "system", null, adminUserId,
                    new()
                    {
                        ["deletedUsers"] = result.DeletedUsers,
                        ["aclsRemoved"] = result.AclsRemoved,
                        ["sharesRevoked"] = result.SharesRevoked
                    }, HttpCtx, ct);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user sync");
            return ServiceError.Server("An unexpected error occurred");
        }
    }

    public async Task<ServiceResult<DeleteUserResponse>> DeleteUserAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.BadRequest("User ID is required");

        var adminUserId = _currentUser.UserId;
        if (string.Equals(userId, adminUserId, StringComparison.OrdinalIgnoreCase))
            return ServiceError.BadRequest("You cannot delete your own account");

        var username = await _userLookup.GetUserNameAsync(userId, ct);

        try
        {
            if (username != null)
            {
                await _keycloakUserService.DeleteUserAsync(userId, ct);
            }
            else
            {
                _logger.LogInformation("User '{UserId}' already absent from Keycloak, cleaning up app data only", userId);
                username = userId;
            }

            var (aclsRemoved, sharesRevoked) = await _cleanupService.CleanupUserDataAsync(userId, ct);

            _logger.LogInformation("Admin deleted user '{Username}' ({UserId}): removed {Acls} ACLs, revoked {Shares} shares",
                username, userId, aclsRemoved, sharesRevoked);

            await _audit.LogAsync("user.deleted", "user",
                Guid.TryParse(userId, out var uid) ? uid : null, adminUserId,
                new()
                {
                    ["username"] = username,
                    ["aclsRemoved"] = aclsRemoved,
                    ["sharesRevoked"] = sharesRevoked
                }, HttpCtx, ct);

            return new DeleteUserResponse
            {
                Message = $"User '{username}' deleted successfully",
                AclsRemoved = aclsRemoved,
                SharesRevoked = sharesRevoked
            };
        }
        catch (KeycloakApiException ex)
        {
            _logger.LogWarning(ex, "Keycloak API error deleting user '{UserId}'", userId);
            return ex.StatusCode == 404
                ? ServiceError.NotFound(ex.Message)
                : ServiceError.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting user '{UserId}'", userId);
            return ServiceError.Server("An unexpected error occurred");
        }
    }
}
