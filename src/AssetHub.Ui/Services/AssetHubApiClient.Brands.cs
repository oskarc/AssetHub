using AssetHub.Application;
using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<List<BrandResponseDto>> GetBrandsAsync(CancellationToken ct = default)
    {
        var result = await brandService.ListAsync(ct);
        return Unwrap(result, "Get brands");
    }

    public async Task<BrandResponseDto> CreateBrandAsync(CreateBrandDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create brand");
        var result = await brandService.CreateAsync(dto, ct);
        return Unwrap(result, "Create brand");
    }

    public async Task<BrandResponseDto> UpdateBrandAsync(Guid id, UpdateBrandDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update brand");
        var result = await brandService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update brand");
    }

    public async Task DeleteBrandAsync(Guid id, CancellationToken ct = default)
    {
        var result = await brandService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete brand");
    }

    public async Task<BrandResponseDto> UploadBrandLogoAsync(
        Guid id, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var result = await brandService.UploadLogoAsync(id, content, fileName, contentType, ct);
        return Unwrap(result, "Upload brand logo");
    }

    public async Task RemoveBrandLogoAsync(Guid id, CancellationToken ct = default)
    {
        var result = await brandService.RemoveLogoAsync(id, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Remove brand logo");
    }

    public async Task AssignBrandToCollectionAsync(Guid brandId, Guid collectionId, CancellationToken ct = default)
    {
        var result = await brandService.AssignToCollectionAsync(brandId, collectionId, ct);
        EnsureSuccess(result, "Assign brand to collection");
    }

    public async Task UnassignBrandFromCollectionAsync(Guid collectionId, CancellationToken ct = default)
    {
        var result = await brandService.UnassignFromCollectionAsync(collectionId, ct);
        EnsureSuccess(result, "Unassign brand from collection");
    }
}
