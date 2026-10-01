using System.Security.Cryptography;
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
    // Audit-detail dictionary keys reused across the upload paths (Sonar S1192).
    private const string KeyTitle = "title";

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
        Guid collectionId, string title, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;

        var preflight = await PreflightUploadAsync(userId, collectionId, fileSize, contentType, ct);
        if (preflight is not null) return preflight;

        if (!await FileMagicValidator.ValidateStreamAsync(fileStream, contentType, ct))
            return ServiceError.BadRequest($"File content does not match the claimed content type '{contentType}'.");

        // Scan for malware (stream position is reset by FileMagicValidator)
        var scanError = await ScanForMalwareAsync(fileStream, fileName, scopeType: "upload", scopeId: Guid.Empty, ct);
        if (scanError is not null) return scanError;

        var sha256Hash = await ComputeSha256Async(fileStream, ct);
        fileStream.Position = 0;

        var asset = CreateAssetEntity(fileName, contentType, fileSize, userId, AssetStatus.Processing);
        asset.Sha256 = sha256Hash;
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

        var linkError = await LinkAssetToCollectionAsync(asset, collectionId, userId, ct);
        if (linkError is not null) return linkError;

        await AuditCreatedAsync(asset, title, collectionId, contentType, userId, ct);
        var jobId = await _mediaProcessing.ScheduleProcessingAsync(asset.Id, asset.AssetType.ToDbString(), asset.OriginalObjectKey, ct);

        return new AssetUploadResult
        {
            Id = asset.Id,
            Status = AssetStatus.Processing.ToDbString(),
            JobId = jobId,
            Message = "Asset uploaded. Processing in progress."
        };
    }

    private async Task<ServiceError?> PreflightUploadAsync(
        string userId, Guid collectionId, long fileSize, string contentType,
        CancellationToken ct)
    {
        var canContribute = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
        if (!canContribute) return ServiceError.Forbidden();

        if (fileSize == 0) return ServiceError.BadRequest("File is required");

        var sizeError = ValidateFileSize(fileSize);
        if (sizeError is not null) return sizeError;

        if (!Constants.AllowedUploadTypes.IsAllowed(contentType))
            return ServiceError.BadRequest($"Content type '{contentType}' is not allowed. Only images, videos, audio, documents, and other safe file types are permitted.");

        return null;
    }

    private async Task<ServiceError?> ScanForMalwareAsync(
        Stream fileStream, string fileName, string scopeType, Guid scopeId, CancellationToken ct)
    {
        var scanResult = await _malwareScanner.ScanAsync(fileStream, fileName, ct);
        if (!scanResult.ScanCompleted)
        {
            _logger.LogError("Malware scan failed for upload {FileName}: {Error}", fileName, scanResult.ErrorMessage);
            return ServiceError.Server("File scanning failed. Please try again later.");
        }
        if (scanResult.IsClean == false)
        {
            _logger.LogWarning("Malware detected in upload {FileName}: {ThreatName}", fileName, scanResult.ThreatName);
            await _audit.LogAsync("asset.malware_detected", scopeType, scopeId, _currentUser.UserId,
                new() { ["fileName"] = fileName, ["threatName"] = scanResult.ThreatName ?? "unknown" }, ct);
            return ServiceError.BadRequest($"File rejected: malware detected ({scanResult.ThreatName}).");
        }
        return null;
    }

    private static async Task<string> ComputeSha256Async(Stream fileStream, CancellationToken ct)
    {
        fileStream.Position = 0;
        var hashBytes = await SHA256.HashDataAsync(fileStream, ct);
        return Convert.ToHexStringLower(hashBytes);
    }

    private async Task<ServiceError?> LinkAssetToCollectionAsync(
        Asset asset, Guid collectionId, string userId, CancellationToken ct)
    {
        try
        {
            await _assetCollectionRepo.AddToCollectionAsync(asset.Id, collectionId, userId, ct);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to link asset {AssetId} to collection {CollectionId} after creation", asset.Id, collectionId);
            asset.MarkFailed("Failed to link asset to collection.");
            await _assetRepo.UpdateAsync(asset, ct);
            return ServiceError.Server("Failed to link asset to collection. Please try again.");
        }
    }

    private Task AuditCreatedAsync(Asset asset, string title, Guid collectionId, string contentType, string userId, CancellationToken ct)
        => _audit.LogAsync("asset.created", Constants.ScopeTypes.Asset, asset.Id, userId,
            new() { [KeyTitle] = title ?? "", ["collectionId"] = collectionId, ["contentType"] = contentType },
            ct);

    public async Task<ServiceResult<InitUploadResponse>> InitUploadAsync(
        InitUploadRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var sizeError = ValidateFileSize(request.FileSize);
        if (sizeError is not null) return sizeError;

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
            new() { [KeyTitle] = request.Title ?? "", ["fileName"] = request.FileName, ["contentType"] = request.ContentType, ["fileSize"] = request.FileSize, ["collectionId"] = request.CollectionId?.ToString() ?? "" },
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

    public async Task<ServiceResult<AssetUploadResult>> ConfirmUploadAsync(Guid id, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset is null)
            return ServiceError.NotFound("Asset not found");

        var preflight = await PreflightConfirmAsync(asset, id, userId, ct);
        if (preflight is not null) return preflight;

        var stat = await _minioAdapter.StatObjectAsync(_bucketName, asset.OriginalObjectKey, ct);
        if (stat is null)
            return ServiceError.BadRequest("File not found in storage. Upload may have failed or expired.");

        var magicError = await ValidateMagicAndCleanupAsync(asset, ct);
        if (magicError is not null) return magicError;

        // Scan for malware (download file from storage for scanning)
        await using var fileStream = await _minioAdapter.DownloadAsync(_bucketName, asset.OriginalObjectKey, ct);
        var scanError = await ScanForMalwareWithCleanupAsync(fileStream, asset, userId, ct);
        if (scanError is not null) return scanError;

        var sha256Hash = await ComputeSha256Async(fileStream, ct);

        asset.Sha256 = sha256Hash;

        asset.SizeBytes = stat.Size;
        asset.Status = AssetStatus.Processing;
        asset.UpdatedAt = DateTime.UtcNow;
        await _assetRepo.UpdateAsync(asset, ct);

        await _audit.LogAsync("asset.upload_confirmed", Constants.ScopeTypes.Asset, asset.Id, userId,
            new() { [KeyTitle] = asset.Title, ["sizeBytes"] = stat.Size }, ct);

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

    private async Task<ServiceError?> PreflightConfirmAsync(
        Asset asset, Guid id, string userId, CancellationToken ct)
    {
        // Allow the original uploader OR any user with Contributor access. Today only the
        // creator confirms (the UI confirms the upload it just started); the Contributor
        // branch served the removed replace/edit flows and is kept unchanged.
        if (asset.CreatedByUserId != userId && !await CanAccessAssetAsync(id, userId, RoleHierarchy.Roles.Contributor, ct))
            return ServiceError.Forbidden();

        if (asset.Status != AssetStatus.Uploading)
            return ServiceError.BadRequest("Asset is not in uploading state");

        return null;
    }

    private async Task<ServiceError?> ValidateMagicAndCleanupAsync(Asset asset, CancellationToken ct)
    {
        // Validate file magic bytes match claimed content type (prevents Content-Type spoofing)
        var headerBytes = await _minioAdapter.DownloadRangeAsync(
            _bucketName, asset.OriginalObjectKey, 0, FileMagicValidator.MinBytesForValidation, ct);
        if (FileMagicValidator.Validate(headerBytes, asset.ContentType)) return null;

        // Delete the spoofed file from storage
        await _minioAdapter.DeleteAsync(_bucketName, asset.OriginalObjectKey, ct);
        await _assetRepo.DeleteAsync(asset.Id, ct);
        return ServiceError.BadRequest($"File content does not match the claimed content type '{asset.ContentType}'.");
    }

    private async Task<ServiceError?> ScanForMalwareWithCleanupAsync(
        Stream fileStream, Asset asset, string userId, CancellationToken ct)
    {
        var scanResult = await _malwareScanner.ScanAsync(fileStream, asset.Title, ct);
        if (!scanResult.ScanCompleted)
        {
            _logger.LogError("Malware scan failed for upload {FileName}: {Error}", asset.Title, scanResult.ErrorMessage);
            return ServiceError.Server("File scanning failed. Please try again later.");
        }
        if (scanResult.IsClean != false) return null;

        _logger.LogWarning("Malware detected in upload {AssetId}/{FileName}: {ThreatName}",
            asset.Id, asset.Title, scanResult.ThreatName);
        await _audit.LogAsync("asset.malware_detected", Constants.ScopeTypes.Asset, asset.Id, userId,
            new() { ["fileName"] = asset.Title, ["threatName"] = scanResult.ThreatName ?? "unknown" }, ct);
        // Delete the infected file
        await _minioAdapter.DeleteAsync(_bucketName, asset.OriginalObjectKey, ct);
        await _assetRepo.DeleteAsync(asset.Id, ct);
        return ServiceError.BadRequest($"File rejected: malware detected ({scanResult.ThreatName}).");
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

    private async Task<bool> CanAccessAssetAsync(Guid assetId, string userId, string requiredRole, CancellationToken ct)
    {
        if (_currentUser.IsSystemAdmin) return true;
        var collectionIds = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        if (collectionIds.Count == 0) return false;
        var accessible = await _authService.FilterAccessibleAsync(userId, collectionIds, requiredRole, ct);
        return accessible.Count > 0;
    }
}
