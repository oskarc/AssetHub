using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>
/// Request DTO for initiating a presigned upload.
/// The server creates an asset record and returns a presigned PUT URL
/// so the browser can upload directly to MinIO.
/// </summary>
public class InitUploadRequest
{
    public Guid? CollectionId { get; set; }

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

/// <summary>
/// Request DTO for saving an edited image as a new copy.
/// The server copies metadata from the source asset, creates a new asset record,
/// and returns a presigned URL for the browser to upload the edited image.
/// </summary>
public class SaveImageCopyRequest
{
    [Required]
    public required string ContentType { get; set; }

    [Required]
    [Range(1, long.MaxValue)]
    public required long FileSize { get; set; }

    [StringLength(255)]
    public string? Title { get; set; }

    /// <summary>
    /// Optional collection to assign the copy to. If null, the copy is not assigned to any collection.
    /// </summary>
    public Guid? CollectionId { get; set; }
}

/// <summary>
/// Request DTO for replacing an existing asset's file with an edited version.
/// Only requires content type and file size — no title or collection since the asset already exists.
/// </summary>
public class ReplaceImageFileRequest
{
    [Required]
    public required string ContentType { get; set; }

    [Required]
    [Range(1, long.MaxValue)]
    public required long FileSize { get; set; }

    /// <summary>
    /// Optional free-text note recorded on the AssetVersion captured before the replace
    /// (T1-VER-01). Surfaces in the version-history panel so users can recall why they
    /// replaced. Capped at 1000 chars to match the DB column.
    /// </summary>
    [StringLength(1000)]
    public string? ChangeNote { get; set; }
}
