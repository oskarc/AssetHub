using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Command operations for export presets: create, update, delete.
/// </summary>
public sealed class ExportPresetService(
    IExportPresetRepository repo,
    CurrentUser currentUser,
    IAuditService auditService,
    IUnitOfWork uow,
    ILogger<ExportPresetService> logger) : IExportPresetService
{
    public async Task<ServiceResult<ExportPresetDto>> CreateAsync(
        CreateExportPresetDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can manage export presets");

        if (!DomainEnumExtensions.IsValidExportPresetFitMode(dto.FitMode))
            return ServiceError.BadRequest($"Invalid fit mode: {dto.FitMode}");

        if (!DomainEnumExtensions.IsValidExportPresetFormat(dto.Format))
            return ServiceError.BadRequest($"Invalid format: {dto.Format}");

        if (await repo.ExistsByNameAsync(dto.Name, ct: ct))
            return ServiceError.Conflict($"An export preset named '{dto.Name}' already exists");

        var preset = new ExportPreset
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            Width = dto.Width,
            Height = dto.Height,
            FitMode = dto.FitMode.ToExportPresetFitMode(),
            Format = dto.Format.ToExportPresetFormat(),
            Quality = dto.Quality,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId!
        };

        // Insert + audit atomic — torn write would leave a preset row
        // with no audit trail of who created it (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await repo.CreateAsync(preset, tct);
            await auditService.LogAsync("exportpreset.created", "exportpreset", preset.Id,
                currentUser.UserId, new Dictionary<string, object> { ["name"] = preset.Name }, tct);
        }, ct);

        logger.LogInformation("Export preset {PresetId} '{PresetName}' created by {UserId}",
            preset.Id, preset.Name, currentUser.UserId);

        return ToDto(preset);
    }

    public async Task<ServiceResult<ExportPresetDto>> UpdateAsync(
        Guid id, UpdateExportPresetDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can manage export presets");

        var preset = await repo.GetByIdForUpdateAsync(id, ct);
        if (preset is null)
            return ServiceError.NotFound("Export preset not found");

        var validation = await ValidateUpdateAsync(id, dto, ct);
        if (validation is not null) return validation;

        ApplyUpdate(preset, dto);

        // Update + audit atomic (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await repo.UpdateAsync(preset, tct);
            await auditService.LogAsync("exportpreset.updated", "exportpreset", preset.Id,
                currentUser.UserId, new Dictionary<string, object> { ["name"] = preset.Name }, tct);
        }, ct);

        logger.LogInformation("Export preset {PresetId} '{PresetName}' updated by {UserId}",
            preset.Id, preset.Name, currentUser.UserId);

        return ToDto(preset);
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can manage export presets");

        var preset = await repo.GetByIdAsync(id, ct);
        if (preset is null)
            return ServiceError.NotFound("Export preset not found");

        // Delete + audit atomic (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await repo.DeleteAsync(id, tct);
            await auditService.LogAsync("exportpreset.deleted", "exportpreset", preset.Id,
                currentUser.UserId, new Dictionary<string, object> { ["name"] = preset.Name }, tct);
        }, ct);

        logger.LogInformation("Export preset {PresetId} '{PresetName}' deleted by {UserId}",
            preset.Id, preset.Name, currentUser.UserId);

        return ServiceResult.Success;
    }

    private async Task<ServiceError?> ValidateUpdateAsync(
        Guid id, UpdateExportPresetDto dto, CancellationToken ct)
    {
        if (dto.FitMode is not null && !DomainEnumExtensions.IsValidExportPresetFitMode(dto.FitMode))
            return ServiceError.BadRequest($"Invalid fit mode: {dto.FitMode}");

        if (dto.Format is not null && !DomainEnumExtensions.IsValidExportPresetFormat(dto.Format))
            return ServiceError.BadRequest($"Invalid format: {dto.Format}");

        if (dto.Name is not null && await repo.ExistsByNameAsync(dto.Name, excludeId: id, ct: ct))
            return ServiceError.Conflict($"An export preset named '{dto.Name}' already exists");

        return null;
    }

    private static void ApplyUpdate(ExportPreset preset, UpdateExportPresetDto dto)
    {
        if (dto.Name is not null) preset.Name = dto.Name.Trim();
        if (dto.Width is not null) preset.Width = dto.Width;
        if (dto.Height is not null) preset.Height = dto.Height;
        if (dto.FitMode is not null) preset.FitMode = dto.FitMode.ToExportPresetFitMode();
        if (dto.Format is not null) preset.Format = dto.Format.ToExportPresetFormat();
        if (dto.Quality is not null) preset.Quality = dto.Quality.Value;
        preset.UpdatedAt = DateTime.UtcNow;
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
