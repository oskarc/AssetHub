using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Command operations for export presets: create, update, delete. Admin-only.
/// For queries, see <see cref="IExportPresetQueryService"/>.
/// </summary>
public interface IExportPresetService
{
    /// <summary>Create a new export preset.</summary>
    Task<ServiceResult<ExportPresetDto>> CreateAsync(CreateExportPresetDto dto, CancellationToken ct);

    /// <summary>Update an existing export preset.</summary>
    Task<ServiceResult<ExportPresetDto>> UpdateAsync(Guid id, UpdateExportPresetDto dto, CancellationToken ct);

    /// <summary>Delete an export preset by ID.</summary>
    Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct);
}
