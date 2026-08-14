using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<AssetSearchResponse> SearchAssetsAsync(AssetSearchRequest request, CancellationToken ct = default)
    {
        Validate(request, "Search assets");
        var result = await assetSearchService.SearchAsync(request, ct);
        return Unwrap(result, "Search assets");
    }
}
