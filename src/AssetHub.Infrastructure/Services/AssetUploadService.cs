using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

public sealed record AssetUploadRepositories(
    IAssetRepository AssetRepo,
    IAssetCollectionRepository AssetCollectionRepo);

public sealed record AssetUploadPipeline(
    IMinIOAdapter MinioAdapter,
    IMalwareScannerService MalwareScanner,
    IMediaProcessingService MediaProcessing,
    string BucketName)
{
    public AssetUploadPipeline(
        IMinIOAdapter minioAdapter,
        IMalwareScannerService malwareScanner,
        IMediaProcessingService mediaProcessing,
        IOptions<MinIOSettings> minioSettings)
        : this(minioAdapter, malwareScanner, mediaProcessing, minioSettings.Value.BucketName) { }
}

/// <summary>
/// Upload operations: streaming upload, presigned upload init/confirm.
/// </summary>
public sealed class AssetUploadService : IAssetUploadService
{
    private readonly IAssetRepository _assetRepo;
    private readonly IAssetCollectionRepository _assetCollectionRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IMinIOAdapter _minioAdapter;
    private readonly IMediaProcessingService _mediaProcessing;
    private readonly IMalwareScannerService _malwareScanner;
    private readonly IAuditService _audit;
    private readonly CurrentUser _currentUser;
    private readonly string _bucketName;
    private readonly int _maxUploadSizeMb;
    private readonly ILogger<AssetUploadService> _logger;

    public AssetUploadService(
        AssetUploadRepositories repos,
        AssetUploadPipeline pipeline,
        ICollectionAuthorizationService authService,
        IAuditService audit,
        CurrentUser currentUser,
        IOptions<AppSettings> appSettings,
        ILogger<AssetUploadService> logger)
    {
        _assetRepo = repos.AssetRepo;
        _assetCollectionRepo = repos.AssetCollectionRepo;
        _authService = authService;
        _minioAdapter = pipeline.MinioAdapter;
        _mediaProcessing = pipeline.MediaProcessing;
        _malwareScanner = pipeline.MalwareScanner;
        _audit = audit;
        _currentUser = currentUser;
        _bucketName = pipeline.BucketName;
        _maxUploadSizeMb = appSettings.Value.MaxUploadSizeMb > 0 
            ? appSettings.Value.MaxUploadSizeMb 
            : Constants.Limits.DefaultMaxUploadSizeMb;
        _logger = logger;
    }


    public async Task<ServiceResult<AssetUploadResult>> UploadAsync(
        Stream fileStream, string fileName, string contentType, long fileSize,
        Guid collectionId, string title, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var canContribute = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
        if (!canContribute)
            return ServiceError.Forbidden();

        if (fileSize == 0)
            return ServiceError.BadRequest("File is required");

        var sizeError = ValidateFileSize(fileSize);
        if (sizeError != null) return sizeError;

        if (!Constants.AllowedUploadTypes.IsAllowed(contentType))
            return ServiceError.BadRequest($"Content type '{contentType}' is not allowed. Only images, videos, audio, documents, and other safe file types are permitted.");

        // Validate file magic bytes match claimed content type (prevents Content-Type spoofing)
        if (!await FileMagicValidator.ValidateStreamAsync(fileStream, contentType, ct))
            return ServiceError.BadRequest($"File content does not match the claimed content type '{contentType}'.");

        // Scan for malware (stream position is reset by FileMagicValidator)
        var scanResult = await _malwareScanner.ScanAsync(fileStream, fileName, ct);
        if (!scanResult.ScanCompleted)
        {
            _logger.LogError("Malware scan failed for upload {FileName}: {Error}", fileName, scanResult.ErrorMessage);
            return ServiceError.Server("File scanning failed. Please try again later.");
        }
        if (scanResult.IsClean == false)
        {
            _logger.LogWarning("Malware detected in upload {FileName}: {ThreatName}", fileName, scanResult.ThreatName);
            await _audit.LogAsync("asset.malware_detected", "upload", Guid.Empty, _currentUser.UserId,
                new() { ["fileName"] = fileName, ["threatName"] = scanResult.ThreatName ?? "unknown" }, ct);
            return ServiceError.BadRequest($"File rejected: malware detected ({scanResult.ThreatName}).");
        }

        var asset = CreateAssetEntity(fileName, contentType, fileSize, userId, AssetStatus.Processing);
        if (!string.IsNullOrEmpty(title))
            asset.Title = title;

        try
        {
            await _minioAdapter.UploadAsync(_bucketName, asset.OriginalObjectKey, fileStream, contentType, ct);
        }
        catch (StorageException ex)
        {
            _logger.LogError(ex, "Storage upload failed for {FileName}", fileName);
            return ServiceError.Server(ex.Message);
        }

        await _assetRepo.CreateAsync(asset, ct);
        await _assetCollectionRepo.AddToCollectionAsync(asset.Id, collectionId, userId, ct);

        await _audit.LogAsync("asset.created", Constants.ScopeTypes.Asset, asset.Id, userId,
            new() { ["title"] = title ?? "", ["collectionId"] = collectionId, ["contentType"] = contentType },
            ct);

        var jobId = await _mediaProcessing.ScheduleProcessingAsync(asset.Id, asset.AssetType.ToDbString(), asset.OriginalObjectKey, ct);

        return new AssetUploadResult
        {
            Id = asset.Id,
            Status = AssetStatus.Processing.ToDbString(),
            JobId = jobId,
            Message = "Asset uploaded. Processing in progress."
        };
    }

    public async Task<ServiceResult<InitUploadResponse>> InitUploadAsync(
        InitUploadRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var sizeError = ValidateFileSize(request.FileSize);
        if (sizeError != null) return sizeError;

        if (!Constants.AllowedUploadTypes.IsAllowed(request.ContentType))
            return ServiceError.BadRequest($"Content type '{request.ContentType}' is not allowed. Only images, videos, audio, documents, and other safe file types are permitted.");

        if (request.CollectionId.HasValue)
        {
            var canContribute = await _authService.CheckAccessAsync(userId, request.CollectionId.Value, RoleHierarchy.Roles.Contributor, ct);
            if (!canContribute)
                return ServiceError.Forbidden();
        }
        else
        {
            // Standalone upload (no collection) requires system admin
            if (!_currentUser.IsSystemAdmin)
                return ServiceError.Forbidden();
        }

        var asset = CreateAssetEntity(request.FileName, request.ContentType, request.FileSize, userId, AssetStatus.Uploading);
        if (!string.IsNullOrEmpty(request.Title))
            asset.Title = request.Title;

        await _assetRepo.CreateAsync(asset, ct);

        if (request.CollectionId.HasValue)
            await _assetCollectionRepo.AddToCollectionAsync(asset.Id, request.CollectionId.Value, userId, ct);

        await _audit.LogAsync("asset.upload_initiated", Constants.ScopeTypes.Asset, asset.Id, userId,
            new() { ["title"] = request.Title ?? "", ["fileName"] = request.FileName, ["contentType"] = request.ContentType, ["fileSize"] = request.FileSize, ["collectionId"] = request.CollectionId?.ToString() ?? "" },
            ct);

        string presignedUrl;
        try
        {
            presignedUrl = await _minioAdapter.GetPresignedUploadUrlAsync(
                _bucketName, asset.OriginalObjectKey, Constants.Limits.PresignedUploadExpirySec, ct);
        }
        catch (StorageException ex)
        {
            _logger.LogError(ex, "Failed to generate presigned upload URL for asset {AssetId}", asset.Id);
            return ServiceError.Server(ex.Message);
        }

        return new InitUploadResponse
        {
            AssetId = asset.Id,
            ObjectKey = asset.OriginalObjectKey,
            UploadUrl = presignedUrl,
            ExpiresInSeconds = Constants.Limits.PresignedUploadExpirySec
        };
    }

    public async Task<ServiceResult<AssetUploadResult>> ConfirmUploadAsync(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

        if (asset.CreatedByUserId != userId)
            return ServiceError.Forbidden();

        if (asset.Status != AssetStatus.Uploading)
            return ServiceError.BadRequest("Asset is not in uploading state");

        var stat = await _minioAdapter.StatObjectAsync(_bucketName, asset.OriginalObjectKey, ct);
        if (stat == null)
            return ServiceError.BadRequest("File not found in storage. Upload may have failed or expired.");

        // Validate file magic bytes match claimed content type (prevents Content-Type spoofing)
        var headerBytes = await _minioAdapter.DownloadRangeAsync(
            _bucketName, asset.OriginalObjectKey, 0, FileMagicValidator.MinBytesForValidation, ct);
        if (!FileMagicValidator.Validate(headerBytes, asset.ContentType))
        {
            // Delete the spoofed file from storage
            await _minioAdapter.DeleteAsync(_bucketName, asset.OriginalObjectKey, ct);
            await _assetRepo.DeleteAsync(asset.Id, ct);
            return ServiceError.BadRequest($"File content does not match the claimed content type '{asset.ContentType}'.");
        }

        // Scan for malware (download file from storage for scanning)
        await using var fileStream = await _minioAdapter.DownloadAsync(_bucketName, asset.OriginalObjectKey, ct);
        var scanResult = await _malwareScanner.ScanAsync(fileStream, asset.Title, ct);
        if (!scanResult.ScanCompleted)
        {
            _logger.LogError("Malware scan failed for upload {FileName}: {Error}", asset.Title, scanResult.ErrorMessage);
            return ServiceError.Server("File scanning failed. Please try again later.");
        }
        if (scanResult.IsClean == false)
        {
            _logger.LogWarning("Malware detected in upload {AssetId}/{FileName}: {ThreatName}",
                asset.Id, asset.Title, scanResult.ThreatName);
            await _audit.LogAsync("asset.malware_detected", Constants.ScopeTypes.Asset, asset.Id, userId,
                new() { ["fileName"] = asset.Title, ["threatName"] = scanResult.ThreatName ?? "unknown" }, ct);
            // Delete the infected file
            await _minioAdapter.DeleteAsync(_bucketName, asset.OriginalObjectKey, ct);
            await _assetRepo.DeleteAsync(asset.Id, ct);
            return ServiceError.BadRequest($"File rejected: malware detected ({scanResult.ThreatName}).");
        }

        asset.SizeBytes = stat.Size;
        asset.Status = AssetStatus.Processing;
        asset.UpdatedAt = DateTime.UtcNow;
        await _assetRepo.UpdateAsync(asset, ct);

        await _audit.LogAsync("asset.upload_confirmed", Constants.ScopeTypes.Asset, asset.Id, userId,
            new() { ["title"] = asset.Title, ["sizeBytes"] = stat.Size }, ct);

        var jobId = await _mediaProcessing.ScheduleProcessingAsync(asset.Id, asset.AssetType.ToDbString(), asset.OriginalObjectKey, ct);

        return new AssetUploadResult
        {
            Id = asset.Id,
            Status = AssetStatus.Processing.ToDbString(),
            SizeBytes = stat.Size,
            JobId = jobId,
            Message = "Upload confirmed. Processing in progress."
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private ServiceError? ValidateFileSize(long fileSize)
    {
        var maxSizeBytes = (long)_maxUploadSizeMb * 1024 * 1024;
        return fileSize > maxSizeBytes
            ? ServiceError.BadRequest($"File size exceeds the maximum allowed size of {_maxUploadSizeMb} MB")
            : null;
    }

    private static Asset CreateAssetEntity(
        string fileName, string contentType, long sizeBytes, string userId, AssetStatus status)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        var assetType = AssetTypeHelper.DetermineAssetType(contentType, extension);
        var assetId = Guid.NewGuid();
        var objectKey = $"originals/{assetId}-{Path.GetFileName(fileName)}";

        return new Asset
        {
            Id = assetId,
            AssetType = assetType,
            Status = status,
            Title = Path.GetFileNameWithoutExtension(fileName),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            OriginalObjectKey = objectKey,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
