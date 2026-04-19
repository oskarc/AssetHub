using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Query operations for taxonomies.
/// </summary>
public interface ITaxonomyQueryService
{
    /// <summary>Gets all taxonomies (summary, without terms).</summary>
    Task<ServiceResult<List<TaxonomySummaryDto>>> GetAllAsync(CancellationToken ct);

    /// <summary>Gets a taxonomy by ID with its terms tree.</summary>
    Task<ServiceResult<TaxonomyDto>> GetByIdAsync(Guid id, CancellationToken ct);
}
