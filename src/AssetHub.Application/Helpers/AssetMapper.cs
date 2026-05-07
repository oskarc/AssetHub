using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;

namespace AssetHub.Application.Helpers;

/// <summary>
/// Maps Asset domain entities to response DTOs.
/// </summary>
public static class AssetMapper
{
    /// <summary>
    /// Maps an Asset entity to an AssetResponseDto.
    /// </summary>
    /// <param name="effectiveWatermarkEnabled">
    /// Resolved Share→Asset→Collection precedence flag (T5-WMK-01). Pass <c>null</c>
    /// (default) when the caller doesn't have the resolution context handy — the
    /// mapper falls back to the asset-level override interpreted as "true → on,
    /// false → off, null → off (because we don't know the collection)". Callers
    /// that need accuracy (asset detail / share-create dialogs) should compute the
    /// effective flag via <c>IWatermarkService.IsWatermarkingEffectiveAsync</c>
    /// and pass it explicitly.
    /// </param>
    public static AssetResponseDto ToDto(
        Asset asset,
        string userRole = RoleHierarchy.Roles.Viewer,
        string? createdByUserName = null,
        int derivativeCount = 0,
        bool includeEditDocument = false,
        bool? effectiveWatermarkEnabled = null)
    {
        return new AssetResponseDto
        {
            Id = asset.Id,
            AssetType = asset.AssetType.ToDbString(),
            Status = asset.Status.ToDbString(),
            Title = asset.Title,
            Description = asset.Description,
            Copyright = asset.Copyright,
            Tags = asset.Tags,
            MetadataJson = asset.MetadataJson,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            Sha256 = asset.Sha256,
            ThumbObjectKey = asset.ThumbObjectKey,
            MediumObjectKey = asset.MediumObjectKey,
            PosterObjectKey = asset.PosterObjectKey,
            CreatedAt = asset.CreatedAt,
            CreatedByUserId = asset.CreatedByUserId,
            CreatedByUserName = createdByUserName,
            UpdatedAt = asset.UpdatedAt,
            SourceAssetId = asset.SourceAssetId,
            HasEditDocument = asset.EditDocument is not null,
            EditDocument = includeEditDocument ? asset.EditDocument : null,
            DerivativeCount = derivativeCount,
            CurrentVersionNumber = asset.CurrentVersionNumber,
            UserRole = userRole,
            DurationSeconds = asset.DurationSeconds,
            WatermarkOverride = asset.WatermarkOverride,
            AssetWatermarkAppliedAt = asset.AssetWatermarkAppliedAt,
            EffectiveWatermarkEnabled = effectiveWatermarkEnabled ?? (asset.WatermarkOverride == true)
        };
    }
}
