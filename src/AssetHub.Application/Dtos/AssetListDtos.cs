namespace AssetHub.Application.Dtos;

/// <summary>
/// Paginated response for assets within a specific collection.
/// </summary>
public class AssetListResponse
{
    public Guid CollectionId { get; set; }
    public int Total { get; set; }
    public List<AssetResponseDto> Items { get; set; } = new();
}

/// <summary>
/// Paginated response for assets across all collections.
/// </summary>
public class AllAssetsListResponse
{
    public int Total { get; set; }
    public List<AssetResponseDto> Items { get; set; } = new();
}

/// <summary>
/// Result returned after uploading or confirming an asset upload.
/// </summary>
public class AssetUploadResult
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "";
    public long? SizeBytes { get; set; }
    public string? JobId { get; set; }
    public string? Message { get; set; }
}
