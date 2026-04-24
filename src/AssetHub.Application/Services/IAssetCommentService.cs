using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Asset-level discussion threads (T3-COL-01). All calls scope authorization
/// through the asset's owning collections: viewer-or-higher to read, editor
/// (contributor-or-higher) to create, author-or-admin to edit / delete.
///
/// Mentioning a user via <c>@username</c> in the body triggers an in-app
/// notification (category <c>mention</c>) + email through T3-NTF-01's
/// pipeline. Unknown usernames are dropped silently — we don't want the
/// comment to fail because of a typo.
/// </summary>
public interface IAssetCommentService
{
    Task<ServiceResult<List<AssetCommentResponseDto>>> ListForAssetAsync(
        Guid assetId, CancellationToken ct);

    Task<ServiceResult<AssetCommentResponseDto>> CreateAsync(
        Guid assetId, CreateAssetCommentDto dto, CancellationToken ct);

    Task<ServiceResult<AssetCommentResponseDto>> UpdateAsync(
        Guid commentId, UpdateAssetCommentDto dto, CancellationToken ct);

    Task<ServiceResult> DeleteAsync(Guid commentId, CancellationToken ct);
}
