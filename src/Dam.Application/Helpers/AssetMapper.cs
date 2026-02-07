using Dam.Application.Dtos;
using Dam.Domain.Entities;

namespace Dam.Application.Helpers;

/// <summary>
/// Maps domain entities to DTOs.
/// Centralizes mapping logic that was previously inline in endpoint handlers.
/// </summary>
public static class AssetMapper
{
    /// <summary>
    /// Maps an Asset entity to an AssetResponseDto.
    /// </summary>
    public static AssetResponseDto ToDto(Asset asset, string userRole = RoleHierarchy.Roles.Viewer)
    {
        return new AssetResponseDto
        {
            Id = asset.Id,
            AssetType = asset.AssetType,
            Status = asset.Status,
            Title = asset.Title,
            Description = asset.Description,
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
            UpdatedAt = asset.UpdatedAt,
            UserRole = userRole
        };
    }
}
