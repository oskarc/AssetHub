using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Handles asset upload operations: presigned uploads, direct uploads, and confirmation.
/// 
/// This interface extracts the upload-related methods from IAssetService to provide
/// a focused contract for upload operations, following the Interface Segregation Principle.
/// 
/// Consumers that only need upload functionality should depend on this interface
/// rather than the full IAssetService.
/// </summary>
public interface IAssetUploadService
{
    /// <summary>
    /// Step 1: Create asset record and return presigned PUT URL for direct browser upload.
    /// The caller uses the returned URL to upload the file directly to storage.
    /// </summary>
    Task<ServiceResult<InitUploadResponse>> InitUploadAsync(
        InitUploadRequest request, CancellationToken ct);

    /// <summary>
    /// Step 2: Confirm that the browser upload completed, trigger media processing.
    /// Call this after the client has successfully uploaded to the presigned URL.
    /// </summary>
    Task<ServiceResult<AssetUploadResult>> ConfirmUploadAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Upload a file directly through the API (synchronous upload path).
    /// Use for small/medium files when presigned upload isn't needed.
    /// </summary>
    Task<ServiceResult<AssetUploadResult>> UploadAsync(
        Stream fileStream, string fileName, string contentType, long fileSize,
        Guid collectionId, string title, CancellationToken ct);

    /// <summary>
    /// Save an edited image as a new copy. Creates a new asset record with metadata
    /// copied from the source, adds it to the same collections, and returns a presigned URL.
    /// </summary>
    Task<ServiceResult<InitUploadResponse>> SaveImageCopyAsync(
        Guid sourceAssetId, SaveImageCopyRequest request, CancellationToken ct);

    /// <summary>
    /// Replace the original file of an existing asset with an edited version.
    /// Returns a presigned URL for the browser to upload the replacement file.
    /// </summary>
    Task<ServiceResult<InitUploadResponse>> ReplaceImageFileAsync(
        Guid assetId, ReplaceImageFileRequest request, CancellationToken ct);
}
