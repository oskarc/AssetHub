using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Manager self-service operations for collection ACLs.
/// Used by collection owners to view and grant access within their own authority level.
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
}

/// <summary>
/// Admin-only collection ACL operations: bypasses manager-level auth checks
/// and supports username-or-ID principal resolution.
/// </summary>
public interface IAdminCollectionAclService
{
    /// <summary>Admin: set access with user-ID-or-username resolution.</summary>
    Task<ServiceResult<AccessUpdatedResponse>> AdminSetAccessAsync(
        Guid collectionId, SetCollectionAccessRequest request, CancellationToken ct);

    /// <summary>Admin: remove access for a principal.</summary>
    Task<ServiceResult<AccessRevokedResponse>> AdminRevokeAccessAsync(
        Guid collectionId, string principalType, string principalId, CancellationToken ct);

    /// <summary>Admin: get all collections with ACLs in hierarchical structure.</summary>
    Task<ServiceResult<List<CollectionAccessDto>>> GetCollectionAccessTreeAsync(CancellationToken ct);
}
