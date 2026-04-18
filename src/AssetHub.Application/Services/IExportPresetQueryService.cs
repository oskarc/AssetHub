using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Read-only operations for export presets: list, get by ID.
/// </summary>
public interface IExportPresetQueryService
{
    /// <summary>Get all export presets ordered by name.</summary>
    Task<ServiceResult<List<ExportPresetDto>>> GetAllAsync(CancellationToken ct);

    /// <summary>Get a single export preset by ID.</summary>
    Task<ServiceResult<ExportPresetDto>> GetByIdAsync(Guid id, CancellationToken ct);
}
