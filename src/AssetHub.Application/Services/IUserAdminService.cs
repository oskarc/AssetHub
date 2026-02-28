using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Read-only admin user queries: lists users with their collection access.
/// </summary>
public interface IUserAdminQueryService
{
    /// <summary>Get all users with collection access summaries.</summary>
    Task<ServiceResult<List<UserAccessSummaryDto>>> GetUsersAsync(CancellationToken ct);

    /// <summary>Get all users from Keycloak with app-level access info.</summary>
    Task<ServiceResult<List<KeycloakUserDto>>> GetKeycloakUsersAsync(CancellationToken ct);
}

/// <summary>
/// Admin user lifecycle management: creation, password reset, sync, and deletion.
/// </summary>
public interface IUserAdminService
{
    /// <summary>Create a new user in Keycloak with optional collection grants and welcome email.</summary>
    Task<ServiceResult<CreateUserResponse>> CreateUserAsync(
        CreateUserRequest request, string baseUrl, CancellationToken ct);

    /// <summary>Send a password reset email to a user via Keycloak.</summary>
    Task<ServiceResult> SendPasswordResetEmailAsync(string userId, CancellationToken ct);

    /// <summary>Sync and clean up users deleted from Keycloak.</summary>
    Task<ServiceResult<UserSyncResult>> SyncDeletedUsersAsync(bool dryRun, CancellationToken ct);

    /// <summary>Delete a user from Keycloak and clean up app data.</summary>
    Task<ServiceResult<DeleteUserResponse>> DeleteUserAsync(string userId, CancellationToken ct);
}
