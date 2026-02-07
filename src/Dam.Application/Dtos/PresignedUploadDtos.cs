using System.ComponentModel.DataAnnotations;

namespace Dam.Application.Dtos;

/// <summary>
/// Request DTO for initiating a presigned upload.
/// The server creates an asset record and returns a presigned PUT URL
/// so the browser can upload directly to MinIO.
/// </summary>
public class InitUploadRequest
{
    [Required]
    public required Guid CollectionId { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string FileName { get; set; }

    [Required]
    public required string ContentType { get; set; }

    [Required]
    [Range(1, long.MaxValue)]
    public required long FileSize { get; set; }

    [StringLength(255)]
    public string? Title { get; set; }
}

/// <summary>
/// Response DTO for init-upload. Contains the presigned PUT URL
/// that the browser uses to upload directly to MinIO.
/// </summary>
public class InitUploadResponse
{
    public Guid AssetId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string UploadUrl { get; set; } = "";
    public int ExpiresInSeconds { get; set; }
}
