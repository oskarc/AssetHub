using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Admin CRUD for share-page branding (T4-BP-01). Single-default
/// invariant is enforced here; deletes cascade only the brand row, not
/// the collections that reference it (FK is <c>SetNull</c>).
/// </summary>
public interface IBrandService
{
    Task<ServiceResult<List<BrandResponseDto>>> ListAsync(CancellationToken ct);

    Task<ServiceResult<BrandResponseDto>> GetByIdAsync(Guid id, CancellationToken ct);

    Task<ServiceResult<BrandResponseDto>> CreateAsync(CreateBrandDto dto, CancellationToken ct);

    Task<ServiceResult<BrandResponseDto>> UpdateAsync(Guid id, UpdateBrandDto dto, CancellationToken ct);

    Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Streams a logo upload to MinIO under <c>brands/{brandId}/logo.{ext}</c>
    /// and stores the resulting object key on the brand. Replaces any
    /// existing logo; the prior key is best-effort deleted from MinIO so
    /// orphans don't accumulate.
    /// </summary>
    Task<ServiceResult<BrandResponseDto>> UploadLogoAsync(
        Guid id, Stream content, string fileName, string contentType, CancellationToken ct);

    /// <summary>Removes the logo (object + key field). No-op when no logo set.</summary>
    Task<ServiceResult<BrandResponseDto>> RemoveLogoAsync(Guid id, CancellationToken ct);

    /// <summary>Assigns this brand to <paramref name="collectionId"/>.</summary>
    Task<ServiceResult> AssignToCollectionAsync(Guid brandId, Guid collectionId, CancellationToken ct);

    /// <summary>Clears the brand assignment from a collection.</summary>
    Task<ServiceResult> UnassignFromCollectionAsync(Guid collectionId, CancellationToken ct);
}
