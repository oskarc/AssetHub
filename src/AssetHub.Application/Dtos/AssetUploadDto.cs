using System.ComponentModel.DataAnnotations;
using AssetHub.Application.Resources;

namespace AssetHub.Application.Dtos;

/// <summary>
/// DTO for uploading a new asset.
/// </summary>
public class AssetUploadDto
{
    [Required]
    public required Guid CollectionId { get; set; }
    
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Title { get; set; }
    
    [StringLength(2000)]
    public string? Description { get; set; }
    
    [MaxLength(Constants.Limits.MaxTagsPerAsset, ErrorMessageResourceType = typeof(ValidationResource), ErrorMessageResourceName = nameof(ValidationResource.Tags_MaxCount))]
    public List<string> Tags { get; set; } = [];
    
    public Dictionary<string, object>? MetadataJson { get; set; }
}
