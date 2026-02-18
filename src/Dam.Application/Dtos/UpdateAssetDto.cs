using System.ComponentModel.DataAnnotations;

namespace Dam.Application.Dtos;

/// <summary>
/// DTO for updating asset metadata.
/// </summary>
public class UpdateAssetDto
{
    [StringLength(255, MinimumLength = 1)]
    public string? Title { get; set; }
    
    [StringLength(2000)]
    public string? Description { get; set; }
    
    [StringLength(500)]
    public string? Copyright { get; set; }
    
    public List<string>? Tags { get; set; }
    
    public Dictionary<string, object>? MetadataJson { get; set; }
}
