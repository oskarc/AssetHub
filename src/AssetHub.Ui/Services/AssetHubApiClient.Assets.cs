using AssetHub.Application;
using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<AssetListResponse> GetAssetsAsync(
        Guid collectionId,
        string? query = null,
        string? type = null,
        string sortBy = Constants.SortBy.CreatedDesc,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var result = await assetQueryService.GetAssetsByCollectionAsync(collectionId, query, type, sortBy, skip, take, ct);
        return Unwrap(result, "Get assets");
    }

    public async Task<AssetResponseDto?> GetAssetAsync(Guid id, CancellationToken ct = default)
    {
        var result = await assetQueryService.GetAssetAsync(id, ct);
        return UnwrapOrNullOn(result, "Get asset", 404);
    }

    public async Task<AssetResponseDto> UpdateAssetAsync(Guid id, UpdateAssetDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update asset");
        var result = await assetService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update asset");
    }

    public async Task<AssetUploadResult> UploadAssetAsync(
        Guid collectionId,
        string title,
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        // Direct upload path through IAssetUploadService — replaces the multipart
        // form post the legacy HttpClient client used. fileSize defaults to -1
        // when unknown; the service prefers it but tolerates -1.
        long fileSize = -1;
        try { if (fileStream.CanSeek) fileSize = fileStream.Length; } catch { /* not seekable */ }

        var result = await assetUploadService.UploadAsync(
            fileStream, fileName, contentType, fileSize, collectionId, title, ct);
        return Unwrap(result, "Upload asset");
    }

    public async Task<InitUploadResponse> InitUploadAsync(
        Guid? collectionId,
        string fileName,
        string contentType,
        long fileSize,
        string? title = null,
        CancellationToken ct = default)
    {
        var request = new InitUploadRequest
        {
            CollectionId = collectionId,
            FileName = fileName,
            ContentType = contentType,
            FileSize = fileSize,
            Title = title ?? Path.GetFileNameWithoutExtension(fileName)
        };
        Validate(request, "Init upload");
        var result = await assetUploadService.InitUploadAsync(request, ct);
        return Unwrap(result, "Init upload");
    }

    public async Task<AssetUploadResult> ConfirmUploadAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetUploadService.ConfirmUploadAsync(assetId, ct);
        return Unwrap(result, "Confirm upload");
    }




    public async Task DeleteAssetAsync(Guid id, Guid? fromCollectionId = null, CancellationToken ct = default)
    {
        var result = await assetService.DeleteAsync(id, fromCollectionId, ct);
        EnsureSuccess(result, "Delete asset");
    }

    public async Task<BulkDeleteAssetsResponse> BulkDeleteAssetsAsync(
        List<Guid> assetIds, Guid? fromCollectionId = null, CancellationToken ct = default)
    {
        var request = new BulkDeleteAssetsRequest { AssetIds = assetIds, FromCollectionId = fromCollectionId };
        Validate(request, "Bulk delete assets");
        var result = await assetService.BulkDeleteAsync(request, ct);
        return Unwrap(result, "Bulk delete assets");
    }

    public async Task<AssetDeletionContextDto> GetAssetDeletionContextAsync(Guid id, CancellationToken ct = default)
    {
        var result = await assetQueryService.GetDeletionContextAsync(id, ct);
        return Unwrap(result, "Get asset deletion context");
    }

    public Task<string> GetPresignedDownloadUrlAsync(Guid assetId, string objectKey, CancellationToken ct = default)
    {
        // Browsers hit the rendition endpoint by URL; the service issues the
        // presigned 302 redirect at request time. Nothing to do here.
        return Task.FromResult($"/api/v1/assets/{assetId}/download");
    }
}
