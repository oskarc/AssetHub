using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface IBrandRepository
{
    Task<List<Brand>> ListAllAsync(CancellationToken ct = default);

    Task<Brand?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Brand?> GetDefaultAsync(CancellationToken ct = default);

    Task<Brand> CreateAsync(Brand brand, CancellationToken ct = default);

    Task UpdateAsync(Brand brand, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Demotes every other brand from default in a single statement.
    /// Used when promoting <paramref name="newDefaultId"/>; service-layer
    /// invariant guarantees only one row carries <c>IsDefault = true</c>.
    /// </summary>
    Task ClearDefaultExceptAsync(Guid newDefaultId, CancellationToken ct = default);
}
