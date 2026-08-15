using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Read-only admin user queries: lists users with their collection access.
/// </summary>
public interface IUserAdminQueryService
{
    /// <summary>Get all users with collection access summaries.</summary>
    Task<ServiceResult<List<UserAccessSummaryDto>>> GetUsersAsync(CancellationToken ct);

    /// <summary>Get all users with app-level access info.</summary>
    Task<ServiceResult<List<DirectoryUserDto>>> GetDirectoryUsersAsync(CancellationToken ct);

    /// <summary>Get paginated users from the identity store with filtering, sorting, and category counts.</summary>
    Task<ServiceResult<PaginatedDirectoryUsersResponse>> GetDirectoryUsersPaginatedAsync(
        string? search, string? category, string? sortBy, bool sortDescending,
        int skip, int take, CancellationToken ct);
}

/// <summary>
/// Admin user lifecycle management: creation, password reset, sync, and deletion.
/// </summary>
public interface IUserAdminService
{
    /// <summary>Create a new user in the identity store with optional collection grants and welcome email.</summary>
    Task<ServiceResult<CreateUserResponse>> CreateUserAsync(
        CreateUserRequest request, string baseUrl, CancellationToken ct);

    /// <summary>Send a password reset email to a user.</summary>
    Task<ServiceResult> SendPasswordResetEmailAsync(string userId, CancellationToken ct);


    /// <summary>Delete a user from the identity store and clean up app data.</summary>
    Task<ServiceResult<DeleteUserResponse>> DeleteUserAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Promote or demote a user to/from the global "admin" role.
    /// Only existing admins (system admins) may call this. The caller cannot
    /// demote themselves — that prevents the last admin from accidentally
    /// locking themselves and the system out of the realm.
    /// </summary>
    Task<ServiceResult> SetAdminAsync(string userId, bool isAdmin, CancellationToken ct);
}
