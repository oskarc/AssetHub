using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Admin-only operations for managing shares: listing, token retrieval, and revocation.
/// </summary>
public interface IShareAdminService
{
    /// <summary>Get shares with pagination and usage statistics.</summary>
    Task<ServiceResult<AdminSharesResponse>> GetAllSharesAsync(int skip, int take, CancellationToken ct);

    /// <summary>Retrieve the decrypted plaintext token for a share.</summary>
    Task<ServiceResult<ShareTokenResponse>> GetShareTokenAsync(Guid shareId, CancellationToken ct);

    /// <summary>Retrieve the decrypted plaintext password for a share (admin only).</summary>
    Task<ServiceResult<SharePasswordResponse>> GetSharePasswordAsync(Guid shareId, CancellationToken ct);

    /// <summary>Revoke a share (admin override — no ownership check).</summary>
    Task<ServiceResult> AdminRevokeShareAsync(Guid shareId, CancellationToken ct);

    /// <summary>Permanently delete a share (must be expired or revoked).</summary>
    Task<ServiceResult> DeleteShareAsync(Guid shareId, CancellationToken ct);

    /// <summary>Permanently delete all shares with the given status ("expired" or "revoked").</summary>
    Task<ServiceResult<int>> BulkDeleteSharesByStatusAsync(string status, CancellationToken ct);
}
