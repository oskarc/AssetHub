using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Groups user-lifecycle service dependencies for <see cref="UserAdminService"/>
/// to keep the constructor parameter count manageable.
/// </summary>
public sealed record UserLifecycleServices(
    IUserLookupService UserLookup,
    IKeycloakUserService KeycloakUserService,
    IUserProvisioningService Provisioning,
    IUserCleanupService CleanupService,
    IUserSyncService SyncService);

/// <summary>
/// Admin user lifecycle management: listing, creation, password reset, sync, and deletion.
/// </summary>
public class UserAdminService : IUserAdminQueryService, IUserAdminService
{
    private const string AuditKeyUsername = "username";
    private const string UnexpectedError = "An unexpected error occurred";

    private readonly ICollectionAclRepository _aclRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly UserLifecycleServices _lifecycle;
    private readonly IAuditService _audit;
    private readonly CurrentUser _currentUser;
    private readonly string _adminUsername;
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(
        ICollectionAclRepository aclRepo,
        ICollectionRepository collectionRepo,
        UserLifecycleServices lifecycle,
        IAuditService audit,
        IOptions<KeycloakSettings> keycloakSettings,
        CurrentUser currentUser,
        ILogger<UserAdminService> logger)
    {
        _aclRepo = aclRepo;
        _collectionRepo = collectionRepo;
        _lifecycle = lifecycle;
        _audit = audit;
        _currentUser = currentUser;
        _adminUsername = keycloakSettings.Value.AdminUsername;
        _logger = logger;
    }

    public async Task<ServiceResult<List<UserAccessSummaryDto>>> GetUsersAsync(CancellationToken ct)
    {
        var allAcls = await _aclRepo.GetAllAsync(ct);
        var allCollections = (await _collectionRepo.GetAllWithAclsAsync(ct)).ToDictionary(c => c.Id);

        var userIds = allAcls.Where(a => a.PrincipalType == PrincipalType.User).Select(a => a.PrincipalId).Distinct().ToList();
        var userNames = await _lifecycle.UserLookup.GetUserNamesAsync(userIds, ct);

        var userAccess = allAcls
            .Where(a => a.PrincipalType == PrincipalType.User)
            .GroupBy(a => a.PrincipalId)
            .Select(g => new UserAccessSummaryDto
            {
                UserId = g.Key,
                UserName = userNames.TryGetValue(g.Key, out var name) ? name : $"Deleted User ({g.Key[..Math.Min(8, g.Key.Length)]})",
                CollectionCount = g.Count(),
                HighestRole = RoleHierarchy.GetHighestRole(g.Select(a => a.Role.ToDbString())),
                Collections = g.Select(a => new UserCollectionAccessDto
                {
                    CollectionId = a.CollectionId,
                    CollectionName = allCollections.TryGetValue(a.CollectionId, out var col) ? col.Name : "Unknown",
                    Role = a.Role.ToDbString()
                }).ToList()
            })
            .OrderBy(u => u.UserName)
            .ToList();

        return userAccess;
    }

    public async Task<ServiceResult<List<KeycloakUserDto>>> GetKeycloakUsersAsync(CancellationToken ct)
    {
        var allUsers = await _lifecycle.UserLookup.GetAllUsersAsync(ct);

        // Filter out Keycloak admin accounts and service accounts
        allUsers = allUsers
            .Where(u => !string.Equals(u.Username, _adminUsername, StringComparison.OrdinalIgnoreCase))
            .Where(u => !u.Username.StartsWith("service-account-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Start Keycloak API call in parallel with EF Core query
        var adminUserIdsTask = _lifecycle.KeycloakUserService.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Admin, ct);
        var allAcls = await _aclRepo.GetAllAsync(ct);
        var adminUserIds = await adminUserIdsTask;

        var userAclGroups = allAcls
            .Where(a => a.PrincipalType == PrincipalType.User)
            .GroupBy(a => a.PrincipalId)
            .ToDictionary(g => g.Key, g => new
            {
                CollectionCount = g.Count(),
                HighestRole = RoleHierarchy.GetHighestRole(g.Select(a => a.Role.ToDbString()))
            });

        var result = allUsers.Select(u =>
        {
            var hasAccess = userAclGroups.TryGetValue(u.Id, out var acl);
            var isAdmin = adminUserIds.Contains(u.Id);
            var (collectionCount, highestRole) = ResolveUserAccess(isAdmin, hasAccess, acl?.CollectionCount ?? 0, acl?.HighestRole);
            return new KeycloakUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                CreatedAt = u.CreatedAt,
                CollectionCount = collectionCount,
                HighestRole = highestRole,
                IsSystemAdmin = isAdmin
            };
        }).ToList();

        return result;
    }

    public async Task<ServiceResult<PaginatedKeycloakUsersResponse>> GetKeycloakUsersPaginatedAsync(
        string? search, string? category, string? sortBy, bool sortDescending,
        int skip, int take, CancellationToken ct)
    {
        // Reuse existing method to get all enriched users
        var allResult = await GetKeycloakUsersAsync(ct);
        if (!allResult.IsSuccess) return allResult.Error!;

        var all = allResult.Value!;

        // Category counts (always computed from unfiltered set)
        var adminCount = all.Count(u => u.IsSystemAdmin);
        var withAccessCount = all.Count(u => !u.IsSystemAdmin && u.CollectionCount > 0);
        var noAccessCount = all.Count(u => !u.IsSystemAdmin && u.CollectionCount == 0);

        // Apply category filter
        IEnumerable<KeycloakUserDto> filtered = category?.ToLowerInvariant() switch
        {
            "admin" => all.Where(u => u.IsSystemAdmin),
            "withaccess" => all.Where(u => !u.IsSystemAdmin && u.CollectionCount > 0),
            "noaccess" => all.Where(u => !u.IsSystemAdmin && u.CollectionCount == 0),
            _ => all
        };

        // Apply text search
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(u =>
                u.Username.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (u.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Sort
        filtered = ApplySort(filtered, sortBy, sortDescending);

        var materialised = filtered.ToList();

        return new PaginatedKeycloakUsersResponse
        {
            Users = materialised.Skip(skip).Take(take).ToList(),
            TotalFiltered = materialised.Count,
            TotalAll = all.Count,
            WithAccessCount = withAccessCount,
            AdminCount = adminCount,
            NoAccessCount = noAccessCount
        };
    }

    private static IEnumerable<KeycloakUserDto> ApplySort(
        IEnumerable<KeycloakUserDto> source, string? sortBy, bool descending)
    {
        return sortBy?.ToLowerInvariant() switch
        {
            "email"           => descending ? source.OrderByDescending(u => u.Email ?? string.Empty)       : source.OrderBy(u => u.Email ?? string.Empty),
            "createdat"       => descending ? source.OrderByDescending(u => u.CreatedAt ?? DateTime.MinValue) : source.OrderBy(u => u.CreatedAt ?? DateTime.MinValue),
            "collectioncount" => descending ? source.OrderByDescending(u => u.CollectionCount)             : source.OrderBy(u => u.CollectionCount),
            "highestrole"     => descending ? source.OrderByDescending(u => u.HighestRole ?? string.Empty)  : source.OrderBy(u => u.HighestRole ?? string.Empty),
            _                 => descending ? source.OrderByDescending(u => u.Username)                    : source.OrderBy(u => u.Username),
        };
    }

    public async Task<ServiceResult<CreateUserResponse>> CreateUserAsync(
        CreateUserRequest request, string baseUrl, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        var firstName = request.FirstName?.Trim() ?? "";
        var lastName = request.LastName?.Trim() ?? "";

        if (!InputValidation.TryValidate(out var errors,
            (AuditKeyUsername, InputValidation.ValidateUsername(username)),
            ("email", InputValidation.ValidateEmail(email)),
            ("firstName", InputValidation.ValidateRequired(firstName, "First name")),
            ("lastName", InputValidation.ValidateRequired(lastName, "Last name"))))
        {
            return ServiceError.Validation("Validation failed", errors);
        }

        // Server-side password generation — admin never sees/types the password
        var password = request.Password;
        if (string.IsNullOrWhiteSpace(password))
            password = AssetHub.Application.Helpers.PasswordGenerator.Generate(20);
        else
        {
            var pwError = InputValidation.ValidatePassword(password);
            if (pwError != null)
                return ServiceError.Validation("Validation failed", new Dictionary<string, string> { ["password"] = pwError });
        }

        var collectionErrors = await _lifecycle.Provisioning.ValidateCollectionsExistAsync(request.InitialCollectionIds, ct);
        if (collectionErrors.Count > 0)
            return ServiceError.Validation("One or more collections not found", collectionErrors);

        try
        {
            var userId = await _lifecycle.KeycloakUserService.CreateUserAsync(
                username, email, firstName, lastName,
                password, true /* always temporary */, ct);

            _logger.LogInformation("Admin created user '{Username}' (ID: {UserId})", username, userId);

            // Assign system admin realm role if requested
            if (request.IsSystemAdmin)
            {
                await _lifecycle.KeycloakUserService.AssignRealmRoleAsync(userId, "admin", ct);
                _logger.LogInformation("Assigned 'admin' realm role to user '{Username}'", username);
            }

            // Grant collection access (skip if system admin since they have implicit access)
            if (!request.IsSystemAdmin && request.InitialCollectionIds.Count > 0)
            {
                var role = RoleHierarchy.ResolveRole(request.InitialRole);
                await _lifecycle.Provisioning.GrantCollectionAccessAsync(request.InitialCollectionIds, userId, role, username, ct);
            }

            // Send password setup email via Keycloak (user sets their own password)
            if (!string.IsNullOrWhiteSpace(email))
                await TrySendPasswordSetupEmailAsync(userId, email, username, ct);

            await _audit.LogAsync("user.created", Constants.ScopeTypes.User,
                Guid.TryParse(userId, out var uid) ? uid : null, _currentUser.UserId,
                new() { [AuditKeyUsername] = username, ["email"] = email, ["isSystemAdmin"] = request.IsSystemAdmin.ToString() },
                ct);

            var message = BuildCreateUserMessage(request);

            return new CreateUserResponse
            {
                UserId = userId,
                Username = username,
                Email = email,
                Message = message
            };
        }
        catch (KeycloakApiException ex)
        {
            _logger.LogWarning(ex, "Keycloak API error creating user '{Username}'", username);

            await _audit.LogAsync("user.create_failed", Constants.ScopeTypes.User, null, _currentUser.UserId,
                new() { [AuditKeyUsername] = username, ["error"] = ex.Message },
                ct);

            return ex.StatusCode == 409
                ? ServiceError.Conflict(ex.Message)
                : ServiceError.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating user '{Username}'", username);
            return ServiceError.Server(UnexpectedError);
        }
    }

    public async Task<ServiceResult> SendPasswordResetEmailAsync(
        string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.BadRequest("User ID is required");

        try
        {
            await _lifecycle.KeycloakUserService.SendExecuteActionsEmailAsync(
                userId, new[] { "UPDATE_PASSWORD" }, lifespan: 86400, ct);

            _logger.LogInformation("Admin sent password reset email for user '{UserId}'", userId);

            await _audit.LogAsync("user.password_reset_email", Constants.ScopeTypes.User,
                Guid.TryParse(userId, out var uid) ? uid : null, _currentUser.UserId,
                new() { ["targetUserId"] = userId },
                ct);

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
            return ServiceError.Server(UnexpectedError);
        }
    }

    public async Task<ServiceResult<UserSyncResult>> SyncDeletedUsersAsync(bool dryRun, CancellationToken ct)
    {
        try
        {
            var result = await _lifecycle.SyncService.SyncDeletedUsersAsync(dryRun, ct);

            if (!dryRun && result.DeletedUsers > 0)
            {
                await _audit.LogAsync("user.sync.completed", "system", null, _currentUser.UserId,
                    new()
                    {
                        ["deletedUsers"] = result.DeletedUsers,
                        ["aclsRemoved"] = result.AclsRemoved,
                        ["sharesRevoked"] = result.SharesRevoked
                    }, ct);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user sync");
            return ServiceError.Server(UnexpectedError);
        }
    }

    public async Task<ServiceResult<DeleteUserResponse>> DeleteUserAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.BadRequest("User ID is required");

        if (string.Equals(userId, _currentUser.UserId, StringComparison.OrdinalIgnoreCase))
            return ServiceError.BadRequest("You cannot delete your own account");

        var username = await _lifecycle.UserLookup.GetUserNameAsync(userId, ct);

        try
        {
            if (username != null)
            {
                await _lifecycle.KeycloakUserService.DeleteUserAsync(userId, ct);
            }
            else
            {
                _logger.LogInformation("User '{UserId}' already absent from Keycloak, cleaning up app data only", userId);
                username = userId;
            }

            var (aclsRemoved, sharesRevoked) = await _lifecycle.CleanupService.CleanupUserDataAsync(userId, ct);

            _logger.LogInformation("Admin deleted user '{Username}' ({UserId}): removed {Acls} ACLs, revoked {Shares} shares",
                username, userId, aclsRemoved, sharesRevoked);

            await _audit.LogAsync("user.deleted", Constants.ScopeTypes.User,
                Guid.TryParse(userId, out var uid) ? uid : null, _currentUser.UserId,
                new()
                {
                    [AuditKeyUsername] = username,
                    ["aclsRemoved"] = aclsRemoved,
                    ["sharesRevoked"] = sharesRevoked
                }, ct);

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
            return ServiceError.Server(UnexpectedError);
        }
    }

    private static (int CollectionCount, string? HighestRole) ResolveUserAccess(
        bool isAdmin, bool hasAccess, int aclCollectionCount, string? aclHighestRole)
    {
        if (isAdmin) return (0, RoleHierarchy.Roles.Admin);
        return hasAccess ? (aclCollectionCount, aclHighestRole) : (0, null);
    }

    private static string BuildCreateUserMessage(CreateUserRequest request)
    {
        if (request.IsSystemAdmin)
            return "User created as system administrator";
        if (request.InitialCollectionIds.Count > 0)
            return $"User created and granted {RoleHierarchy.ResolveRole(request.InitialRole)} access to {request.InitialCollectionIds.Count} collection(s)";
        return "User created successfully";
    }

    private async Task TrySendPasswordSetupEmailAsync(
        string userId, string email, string username, CancellationToken ct)
    {
        try
        {
            await _lifecycle.KeycloakUserService.SendExecuteActionsEmailAsync(
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
}
