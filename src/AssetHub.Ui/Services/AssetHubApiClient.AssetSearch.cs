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

    public async Task<List<SavedSearchDto>> GetSavedSearchesAsync(CancellationToken ct = default)
    {
        var result = await savedSearchService.GetMineAsync(ct);
        return Unwrap(result, "Get saved searches");
    }

    public async Task<SavedSearchDto?> GetSavedSearchAsync(Guid id, CancellationToken ct = default)
    {
        var result = await savedSearchService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get saved search", 404);
    }

    public async Task<SavedSearchDto> CreateSavedSearchAsync(CreateSavedSearchDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create saved search");
        var result = await savedSearchService.CreateAsync(dto, ct);
        return Unwrap(result, "Create saved search");
    }

    public async Task<SavedSearchDto> UpdateSavedSearchAsync(Guid id, UpdateSavedSearchDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update saved search");
        var result = await savedSearchService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update saved search");
    }

    public async Task DeleteSavedSearchAsync(Guid id, CancellationToken ct = default)
    {
        var result = await savedSearchService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete saved search");
    }
}
