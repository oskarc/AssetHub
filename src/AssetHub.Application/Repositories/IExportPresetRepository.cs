using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Repository interface for ExportPreset entities.
/// </summary>
public interface IExportPresetRepository
{
    /// <summary>Gets an export preset by ID (may be cached, do not mutate).</summary>
    Task<ExportPreset?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gets an export preset by ID with tracking for update operations.</summary>
    Task<ExportPreset?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gets all export presets ordered by name.</summary>
    Task<List<ExportPreset>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Gets multiple export presets by their IDs.</summary>
    Task<List<ExportPreset>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>Checks if a preset with the given name already exists (case-insensitive).</summary>
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>Creates a new export preset.</summary>
    Task<ExportPreset> CreateAsync(ExportPreset preset, CancellationToken ct = default);

    /// <summary>Updates an existing export preset.</summary>
    Task<ExportPreset> UpdateAsync(ExportPreset preset, CancellationToken ct = default);

    /// <summary>Deletes an export preset.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
