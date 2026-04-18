using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Read-only operations for export presets.
/// </summary>
public sealed class ExportPresetQueryService(
    IExportPresetRepository repo) : IExportPresetQueryService
{
    public async Task<ServiceResult<List<ExportPresetDto>>> GetAllAsync(CancellationToken ct)
    {
        var presets = await repo.GetAllAsync(ct);

        return presets.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<ExportPresetDto>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var preset = await repo.GetByIdAsync(id, ct);
        if (preset is null)
            return ServiceError.NotFound("Export preset not found");

        return ToDto(preset);
    }

    private static ExportPresetDto ToDto(ExportPreset preset) => new()
    {
        Id = preset.Id,
        Name = preset.Name,
        FitMode = preset.FitMode.ToDbString(),
        Format = preset.Format.ToDbString(),
        Width = preset.Width,
        Height = preset.Height,
        Quality = preset.Quality,
        CreatedAt = preset.CreatedAt,
        UpdatedAt = preset.UpdatedAt,
        CreatedByUserId = preset.CreatedByUserId
    };
}
