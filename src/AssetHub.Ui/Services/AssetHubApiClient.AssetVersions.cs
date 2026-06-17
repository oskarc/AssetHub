using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<List<AssetVersionDto>> GetAssetVersionsAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetVersionService.GetForAssetAsync(assetId, ct);
        return Unwrap(result, "Get asset versions");
    }

    public async Task<AssetVersionDto> RestoreAssetVersionAsync(Guid assetId, int versionNumber, CancellationToken ct = default)
    {
        var result = await assetVersionService.RestoreAsync(assetId, versionNumber, ct);
        return Unwrap(result, "Restore asset version");
    }

    public async Task PruneAssetVersionAsync(Guid assetId, int versionNumber, CancellationToken ct = default)
    {
        var result = await assetVersionService.PruneAsync(assetId, versionNumber, ct);
        EnsureSuccess(result, "Prune asset version");
    }
}
