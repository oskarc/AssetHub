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
public sealed class UserAdminService(
    ICollectionAclRepository aclRepo,
    ICollectionRepository collectionRepo,
    UserLifecycleServices lifecycle,
    IAuditService audit,
    IOptions<KeycloakSettings> keycloakSettings,
    CurrentUser currentUser,
    ILogger<UserAdminService> logger) : IUserAdminQueryService, IUserAdminService
{
    private const string AuditKeyUsername = "username";
    private const string UnexpectedError = "An unexpected error occurred";

    private readonly string _adminUsername = keycloakSettings.Value.AdminUsername;

    public async Task<ServiceResult<List<UserAccessSummaryDto>>> GetUsersAsync(CancellationToken ct)
    {
        var allAcls = await aclRepo.GetAllAsync(ct);
        var allCollections = (await collectionRepo.GetAllWithAclsAsync(ct)).ToDictionary(c => c.Id);

        var userIds = allAcls.Where(a => a.PrincipalType == PrincipalType.User).Select(a => a.PrincipalId).Distinct().ToList();
        var userNames = await lifecycle.UserLookup.GetUserNamesAsync(userIds, ct);

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
        var allUsers = await lifecycle.UserLookup.GetAllUsersAsync(ct);

        // Filter out Keycloak admin accounts and service accounts
        allUsers = allUsers
            .Where(u => !string.Equals(u.Username, _adminUsername, StringComparison.OrdinalIgnoreCase))
            .Where(u => !u.Username.StartsWith("service-account-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Start Keycloak API call in parallel with EF Core query
        var adminUserIdsTask = lifecycle.KeycloakUserService.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Admin, ct);
        var allAcls = await aclRepo.GetAllAsync(ct);
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
            if (pwError is not null)
                return ServiceError.Validation("Validation failed", new Dictionary<string, string> { ["password"] = pwError });
        }

        var collectionErrors = await lifecycle.Provisioning.ValidateCollectionsExistAsync(request.InitialCollectionIds, ct);
        if (collectionErrors.Count > 0)
            return ServiceError.Validation("One or more collections not found", collectionErrors);

        try
        {
            var userId = await lifecycle.KeycloakUserService.CreateUserAsync(
                username, email, firstName, lastName,
                password, true /* always temporary */, ct);

            logger.LogInformation("Admin created user '{Username}' (ID: {UserId})", username, userId);

            // Assign system admin realm role if requested
            if (request.IsSystemAdmin)
            {
                await lifecycle.KeycloakUserService.AssignRealmRoleAsync(userId, "admin", ct);
                logger.LogInformation("Assigned 'admin' realm role to user '{Username}'", username);
            }

            // Grant collection access (skip if system admin since they have implicit access)
            if (!request.IsSystemAdmin && request.InitialCollectionIds.Count > 0)
            {
                var role = RoleHierarchy.ResolveRole(request.InitialRole);
                await lifecycle.Provisioning.GrantCollectionAccessAsync(request.InitialCollectionIds, userId, role, username, ct);
            }

            // Send password setup email via Keycloak (user sets their own password)
            if (!string.IsNullOrWhiteSpace(email))
                await TrySendPasswordSetupEmailAsync(userId, email, username, ct);

            await audit.LogAsync("user.created", Constants.ScopeTypes.User,
                Guid.TryParse(userId, out var uid) ? uid : null, currentUser.UserId,
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
            logger.LogWarning(ex, "Keycloak API error creating user '{Username}'", username);

            await audit.LogAsync("user.create_failed", Constants.ScopeTypes.User, null, currentUser.UserId,
                new() { [AuditKeyUsername] = username, ["error"] = ex.Message },
                ct);

            return ex.StatusCode == 409
                ? ServiceError.Conflict(ex.Message)
                : ServiceError.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error creating user '{Username}'", username);
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
            await lifecycle.KeycloakUserService.SendExecuteActionsEmailAsync(
                userId, new[] { "UPDATE_PASSWORD" }, lifespan: 86400, ct);

            logger.LogInformation("Admin sent password reset email for user '{UserId}'", userId);

            await audit.LogAsync("user.password_reset_email", Constants.ScopeTypes.User,
                Guid.TryParse(userId, out var uid) ? uid : null, currentUser.UserId,
                new() { ["targetUserId"] = userId },
                ct);

            return ServiceResult.Success;
        }
        catch (KeycloakApiException ex)
        {
            logger.LogWarning(ex, "Keycloak API error sending password reset email for user '{UserId}'", userId);
            return ex.StatusCode == 404
                ? ServiceError.NotFound(ex.Message)
                : ServiceError.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error sending password reset email for user '{UserId}'", userId);
            return ServiceError.Server(UnexpectedError);
        }
    }

    public async Task<ServiceResult<UserSyncResult>> SyncDeletedUsersAsync(bool dryRun, CancellationToken ct)
    {
        try
        {
            var result = await lifecycle.SyncService.SyncDeletedUsersAsync(dryRun, ct);

            if (!dryRun && result.DeletedUsers > 0)
            {
                await audit.LogAsync("user.sync.completed", "system", null, currentUser.UserId,
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
            logger.LogError(ex, "Error during user sync");
            return ServiceError.Server(UnexpectedError);
        }
    }

    public async Task<ServiceResult<DeleteUserResponse>> DeleteUserAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.BadRequest("User ID is required");

        if (string.Equals(userId, currentUser.UserId, StringComparison.OrdinalIgnoreCase))
            return ServiceError.BadRequest("You cannot delete your own account");

        var username = await lifecycle.UserLookup.GetUserNameAsync(userId, ct);

        try
        {
            if (username is not null)
            {
                await lifecycle.KeycloakUserService.DeleteUserAsync(userId, ct);
            }
            else
            {
                logger.LogInformation("User '{UserId}' already absent from Keycloak, cleaning up app data only", userId);
                username = userId;
            }

            var (aclsRemoved, sharesRevoked) = await lifecycle.CleanupService.CleanupUserDataAsync(userId, ct);

            logger.LogInformation("Admin deleted user '{Username}' ({UserId}): removed {Acls} ACLs, revoked {Shares} shares",
                username, userId, aclsRemoved, sharesRevoked);

            await audit.LogAsync("user.deleted", Constants.ScopeTypes.User,
                Guid.TryParse(userId, out var uid) ? uid : null, currentUser.UserId,
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
            logger.LogWarning(ex, "Keycloak API error deleting user '{UserId}'", userId);
            return ex.StatusCode == 404
                ? ServiceError.NotFound(ex.Message)
                : ServiceError.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error deleting user '{UserId}'", userId);
            return ServiceError.Server(UnexpectedError);
        }
    }

    public async Task<ServiceResult> SetAdminAsync(string userId, bool isAdmin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.BadRequest("User ID is required");

        // Block self-demotion. The last admin demoting themselves locks the
        // realm out of admin operations from the AssetHub UI; another admin
        // (or a Keycloak operator) is required to recover. Promotion of self
        // is also unnecessary since the caller is already admin to be here.
        if (string.Equals(userId, currentUser.UserId, StringComparison.OrdinalIgnoreCase))
            return ServiceError.BadRequest(isAdmin
                ? "You are already an administrator."
                : "You cannot remove the administrator role from your own account.");

        var username = await lifecycle.UserLookup.GetUserNameAsync(userId, ct) ?? userId;

        try
        {
            // Mutation + audit are intentionally NOT wrapped in IUnitOfWork:
            // the Keycloak Admin API is the source of truth and rolls back
            // independently if it fails. The audit row is best-effort.
            if (isAdmin)
                await lifecycle.KeycloakUserService.AssignRealmRoleAsync(userId, RoleHierarchy.Roles.Admin, ct);
            else
                await lifecycle.KeycloakUserService.RemoveRealmRoleAsync(userId, RoleHierarchy.Roles.Admin, ct);

            var auditEvent = isAdmin ? "user.promoted_to_admin" : "user.demoted_from_admin";
            await audit.LogAsync(auditEvent, Constants.ScopeTypes.User,
                Guid.TryParse(userId, out var uid) ? uid : null, currentUser.UserId,
                new() { [AuditKeyUsername] = username }, ct);

            logger.LogInformation(
                "Admin {ActorId} {Action} user '{Username}' ({UserId})",
                currentUser.UserId,
                isAdmin ? "promoted to admin" : "demoted from admin",
                username, userId);

            return ServiceResult.Success;
        }
        catch (KeycloakApiException ex)
        {
            logger.LogWarning(ex, "Keycloak API error setting admin role on user '{UserId}'", userId);
            return ex.StatusCode == 404
                ? ServiceError.NotFound(ex.Message)
                : ServiceError.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error setting admin role on user '{UserId}'", userId);
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
            await lifecycle.KeycloakUserService.SendExecuteActionsEmailAsync(
                userId, new[] { "UPDATE_PASSWORD" }, lifespan: 86400, ct);
            logger.LogInformation(
                "Sent password setup email to '{Email}' for new user '{Username}'",
                email, username);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send password setup email to '{Email}' for new user '{Username}'",
                email, username);
        }
    }
}
