using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Command operations for taxonomies (admin only).
/// </summary>
public interface ITaxonomyService
{
    /// <summary>Creates a new taxonomy with optional terms.</summary>
    Task<ServiceResult<TaxonomyDto>> CreateAsync(CreateTaxonomyDto dto, CancellationToken ct);

    /// <summary>Updates a taxonomy name/description.</summary>
    Task<ServiceResult<TaxonomyDto>> UpdateAsync(Guid id, UpdateTaxonomyDto dto, CancellationToken ct);

    /// <summary>Replaces the full term tree for a taxonomy.</summary>
    Task<ServiceResult<TaxonomyDto>> ReplaceTermsAsync(Guid id, List<UpsertTaxonomyTermDto> terms, CancellationToken ct);

    /// <summary>Deletes a taxonomy. Fails if referenced by metadata fields.</summary>
    Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct);
}
