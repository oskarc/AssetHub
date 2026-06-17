using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<List<AssetCommentResponseDto>> GetAssetCommentsAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetCommentService.ListForAssetAsync(assetId, ct);
        return Unwrap(result, "Get asset comments");
    }

    public async Task<AssetCommentResponseDto> CreateAssetCommentAsync(
        Guid assetId, CreateAssetCommentDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create asset comment");
        var result = await assetCommentService.CreateAsync(assetId, dto, ct);
        return Unwrap(result, "Create asset comment");
    }

    public async Task<AssetCommentResponseDto> UpdateAssetCommentAsync(
        Guid assetId, Guid commentId, UpdateAssetCommentDto dto, CancellationToken ct = default)
    {
        // assetId kept for caller-side context; the service identifies the comment
        // by its own primary key and re-checks the asset relationship internally.
        _ = assetId;
        Validate(dto, "Update asset comment");
        var result = await assetCommentService.UpdateAsync(commentId, dto, ct);
        return Unwrap(result, "Update asset comment");
    }

    public async Task DeleteAssetCommentAsync(Guid assetId, Guid commentId, CancellationToken ct = default)
    {
        _ = assetId;
        var result = await assetCommentService.DeleteAsync(commentId, ct);
        EnsureSuccess(result, "Delete asset comment");
    }

    /// <summary>
    /// Resolve a batch of user IDs to display names. Backed by the same
    /// Keycloak-cached lookup the server uses for audit-event display.
    /// Used by the comments panel + user-search autocomplete to avoid
    /// rendering raw Keycloak subs.
    /// </summary>
    public async Task<Dictionary<string, string>> GetUserNamesAsync(
        IEnumerable<string> userIds, CancellationToken ct = default)
    {
        return await userLookupService.GetUserNamesAsync(userIds, ct);
    }

    /// <summary>
    /// Search users by username/email prefix. Used by the comment editor's
    /// @mention autocomplete dropdown. Cap is small by design — the editor
    /// only wants enough hits to render a usable picker.
    /// </summary>
    public async Task<List<UserSearchResultDto>> SearchUsersForMentionAsync(
        string query, int take = 10, CancellationToken ct = default)
    {
        var rows = await userLookupService.SearchUsersAsync(query, take, ct);
        return rows.Select(r => new UserSearchResultDto
        {
            Id = r.Id,
            Username = r.Username,
            Email = r.Email
        }).ToList();
    }
}
