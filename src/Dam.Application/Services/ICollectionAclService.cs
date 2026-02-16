using Dam.Application.Dtos;

namespace Dam.Application.Services;

/// <summary>
/// Unified service for collection ACL management — used by both regular
/// collection endpoints (manager self-service) and admin endpoints.
/// Eliminates the duplicated ACL logic that previously lived in two endpoint files.
/// </summary>
public interface ICollectionAclService
{
    /// <summary>Get ACLs for a collection (requires manager+ access).</summary>
    Task<ServiceResult<IEnumerable<CollectionAclResponseDto>>> GetAclsAsync(
        Guid collectionId, CancellationToken ct);

    /// <summary>Set (create/update) access for a principal on a collection.</summary>
    Task<ServiceResult<CollectionAclResponseDto>> SetAccessAsync(
        Guid collectionId, string principalType, string principalId, string role,
        CancellationToken ct);

    /// <summary>Revoke access for a principal on a collection.</summary>
    Task<ServiceResult> RevokeAccessAsync(
        Guid collectionId, string principalType, string principalId,
        CancellationToken ct);

    /// <summary>Search users that can be added to a collection's ACL.</summary>
    Task<ServiceResult<List<UserSearchResultDto>>> SearchUsersForAclAsync(
        Guid collectionId, string? query, CancellationToken ct);

    // ── Admin-only operations ────────────────────────────────────────────────

    /// <summary>Admin: set access with user-ID-or-username resolution.</summary>
    Task<ServiceResult<AccessUpdatedResponse>> AdminSetAccessAsync(
        Guid collectionId, SetCollectionAccessRequest request, CancellationToken ct);

    /// <summary>Admin: remove access for a principal.</summary>
    Task<ServiceResult<AccessRevokedResponse>> AdminRevokeAccessAsync(
        Guid collectionId, string principalType, string principalId, CancellationToken ct);

    /// <summary>Admin: get all collections with ACLs in hierarchical structure.</summary>
    Task<ServiceResult<List<CollectionAccessDto>>> GetCollectionAccessTreeAsync(CancellationToken ct);
}
