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
    public static AssetResponseDto ToDto(Asset asset, string userRole = RoleHierarchy.Roles.Viewer, string? createdByUserName = null)
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
            UserRole = userRole
        };
    }
}
