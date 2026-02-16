using Dam.Application.Dtos;

namespace Dam.Application.Services;

/// <summary>
/// Orchestrates admin-only operations: share management, user management,
/// Keycloak integration, and user sync.
/// </summary>
public interface IAdminService
{
    // ── Share Management ─────────────────────────────────────────────────────

    /// <summary>Get all shares with usage statistics.</summary>
    Task<ServiceResult<List<AdminShareDto>>> GetAllSharesAsync(CancellationToken ct);

    /// <summary>Retrieve the decrypted plaintext token for a share.</summary>
    Task<ServiceResult<ShareTokenResponse>> GetShareTokenAsync(Guid shareId, CancellationToken ct);

    /// <summary>Revoke a share (admin override — no ownership check).</summary>
    Task<ServiceResult> AdminRevokeShareAsync(Guid shareId, CancellationToken ct);

    // ── User Management ──────────────────────────────────────────────────────

    /// <summary>Get all users with collection access summaries.</summary>
    Task<ServiceResult<List<UserAccessSummaryDto>>> GetUsersAsync(CancellationToken ct);

    /// <summary>Get all users from Keycloak with app-level access info.</summary>
    Task<ServiceResult<List<KeycloakUserDto>>> GetKeycloakUsersAsync(CancellationToken ct);

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
