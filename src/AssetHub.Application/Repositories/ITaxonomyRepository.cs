using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface ITaxonomyRepository
{
    /// <summary>Gets a taxonomy by ID with all terms (cached, no tracking).</summary>
    Task<Taxonomy?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gets a taxonomy by ID with terms (tracked, for update).</summary>
    Task<Taxonomy?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gets all taxonomies without terms (cached).</summary>
    Task<List<Taxonomy>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Checks if a taxonomy with the given name already exists.</summary>
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>Checks if any metadata fields reference this taxonomy.</summary>
    Task<bool> IsReferencedByFieldsAsync(Guid taxonomyId, CancellationToken ct = default);

    /// <summary>Gets terms by their IDs.</summary>
    Task<List<TaxonomyTerm>> GetTermsByIdsAsync(IEnumerable<Guid> termIds, CancellationToken ct = default);

    /// <summary>Creates a new taxonomy with optional terms.</summary>
    Task<Taxonomy> CreateAsync(Taxonomy taxonomy, CancellationToken ct = default);

    /// <summary>Updates an existing taxonomy (must be tracked).</summary>
    Task<Taxonomy> UpdateAsync(Taxonomy taxonomy, CancellationToken ct = default);

    /// <summary>Deletes a taxonomy and all its terms.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Replaces all terms for a taxonomy.</summary>
    Task ReplaceTermsAsync(Guid taxonomyId, ICollection<TaxonomyTerm> terms, CancellationToken ct = default);
}
