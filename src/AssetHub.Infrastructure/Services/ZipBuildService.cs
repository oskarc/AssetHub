using System.IO.Compression;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wolverine;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Groups data-access dependencies for <see cref="ZipBuildService"/>.
/// </summary>
public sealed record ZipBuildDataDependencies(
    IDbContextFactory<AssetHubDbContext> DbFactory,
    IAssetRepository AssetRepo,
    ICollectionRepository CollectionRepo);

/// <summary>
/// Manages queued ZIP download builds via Wolverine.
/// Builds ZIP files in the background and stores them as temporary MinIO objects.
/// </summary>
public sealed class ZipBuildService(
    ZipBuildDataDependencies data,
    IMinIOAdapter minioAdapter,
    IMessageBus messageBus,
    IAuditService audit,
    IOptions<MinIOSettings> minioSettings,
    ILogger<ZipBuildService> logger) : IZipBuildService
{
    private readonly IDbContextFactory<AssetHubDbContext> _dbFactory = data.DbFactory;
    private readonly IAssetRepository _assetRepo = data.AssetRepo;
    private readonly ICollectionRepository _collectionRepo = data.CollectionRepo;
    private readonly string _bucketName = minioSettings.Value.BucketName;


    public async Task<ServiceResult<ZipDownloadEnqueuedResponse>> EnqueueCollectionZipAsync(
        Guid collectionId, string userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Throttle: limit concurrent builds per user
        var activeCount = await db.ZipDownloads
            .CountAsync(z => z.RequestedByUserId == userId
                && (z.Status == ZipDownloadStatus.Pending || z.Status == ZipDownloadStatus.Building), ct);

        if (activeCount >= Constants.Limits.MaxConcurrentZipBuilds)
            return ServiceError.BadRequest(
                $"You already have {activeCount} ZIP downloads in progress. Please wait for them to complete.");

        var collection = await _collectionRepo.GetByIdAsync(collectionId, ct: ct);
        if (collection is null)
            return ServiceError.NotFound("Collection not found");

        var zipFileName = $"{collection.Name.Replace(" ", "_")}_assets.zip";
        var zipDownload = CreateZipDownloadRecord(collectionId, Constants.ScopeTypes.Collection, zipFileName, userId: userId);

        db.ZipDownloads.Add(zipDownload);
        await db.SaveChangesAsync(ct);

        await messageBus.PublishAsync(new BuildZipCommand { ZipDownloadId = zipDownload.Id });

        logger.LogInformation("Enqueued ZIP build {ZipDownloadId} for collection {CollectionId} by user {UserId}",
            zipDownload.Id, collectionId, userId);

        return new ZipDownloadEnqueuedResponse
        {
            JobId = zipDownload.Id,
            StatusUrl = $"/api/v1/zip-downloads/{zipDownload.Id}",
            Message = "ZIP download queued. Poll the status URL for progress."
        };
    }

    public async Task<ServiceResult<ZipDownloadEnqueuedResponse>> EnqueueShareZipAsync(
        Guid collectionId, string shareTokenHash, string collectionName, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Throttle: limit concurrent builds per share token
        var activeCount = await db.ZipDownloads
            .CountAsync(z => z.ShareTokenHash == shareTokenHash
                && (z.Status == ZipDownloadStatus.Pending || z.Status == ZipDownloadStatus.Building), ct);

        if (activeCount >= Constants.Limits.MaxConcurrentZipBuilds)
            return ServiceError.BadRequest(
                "Too many ZIP downloads in progress for this share. Please wait.");

        var zipFileName = $"{collectionName.Replace(" ", "_")}_assets.zip";
        var zipDownload = CreateZipDownloadRecord(collectionId, Constants.ScopeTypes.Collection, zipFileName, shareTokenHash: shareTokenHash);

        db.ZipDownloads.Add(zipDownload);
        await db.SaveChangesAsync(ct);

        await messageBus.PublishAsync(new BuildZipCommand { ZipDownloadId = zipDownload.Id });

        logger.LogInformation("Enqueued ZIP build {ZipDownloadId} for shared collection {CollectionId}",
            zipDownload.Id, collectionId);

        return new ZipDownloadEnqueuedResponse
        {
            JobId = zipDownload.Id,
            StatusUrl = $"/api/v1/zip-downloads/{zipDownload.Id}/share",
            Message = "ZIP download queued. Poll the status URL for progress."
        };
    }

    public async Task<ServiceResult<ZipDownloadStatusResponse>> GetStatusAsync(
        Guid jobId, string? userId, string? shareTokenHash, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var zip = await db.ZipDownloads.FirstOrDefaultAsync(z => z.Id == jobId, ct);
        if (zip is null)
            return ServiceError.NotFound("ZIP download not found");

        // Ensure the requester owns this download
        if (userId is not null && zip.RequestedByUserId != userId)
            return ServiceError.Forbidden();
        if (shareTokenHash is not null && zip.ShareTokenHash != shareTokenHash)
            return ServiceError.Forbidden();
        if (userId is null && shareTokenHash is null)
            return ServiceError.Forbidden();

        var response = new ZipDownloadStatusResponse
        {
            JobId = zip.Id,
            Status = zip.Status.ToDbString(),
            FileName = zip.ZipFileName,
            AssetCount = zip.AssetCount,
            SizeBytes = zip.SizeBytes,
            Error = zip.ErrorMessage
        };

        if (zip.Status == ZipDownloadStatus.Completed && !string.IsNullOrEmpty(zip.ZipObjectKey))
        {
            // Check if still within expiry
            if (zip.ExpiresAt < DateTime.UtcNow)
            {
                return response with { Status = "expired", Error = "This download has expired." };
            }

            var downloadUrl = await minioAdapter.GetPresignedDownloadUrlAsync(
                _bucketName, zip.ZipObjectKey,
                expirySeconds: (int)(zip.ExpiresAt - DateTime.UtcNow).TotalSeconds,
                forceDownload: true,
                downloadFileName: zip.ZipFileName,
                ct);

            response = response with
            {
                DownloadUrl = downloadUrl,
                ExpiresAt = zip.ExpiresAt
            };
        }

        return response;
    }

    public async Task BuildZipAsync(Guid zipDownloadId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var zip = await db.ZipDownloads.FirstOrDefaultAsync(z => z.Id == zipDownloadId, ct);
        if (zip is null)
        {
            logger.LogWarning("ZIP download record {ZipDownloadId} not found, skipping", zipDownloadId);
            return;
        }

        zip.Status = ZipDownloadStatus.Building;
        await db.SaveChangesAsync(ct);

        try
        {
            var assetList = await LoadDownloadableAssetsAsync(zip, db, ct);
            if (assetList.Count == 0) return;

            await BuildAndUploadZipAsync(zip, zipDownloadId, assetList, db, ct);
        }
        catch (OperationCanceledException)
        {
            await MarkFailedAsync(zip, "Build was cancelled", db);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ZIP build {ZipDownloadId} failed", zipDownloadId);
            await MarkFailedAsync(zip, "An error occurred while building the ZIP file", db);
        }
    }

    private async Task<List<Asset>> LoadDownloadableAssetsAsync(
        ZipDownload zip, AssetHubDbContext db, CancellationToken ct)
    {
        var assets = await _assetRepo.GetByCollectionAsync(
            zip.ScopeId, 0, Constants.Limits.MaxDownloadableAssets, ct);
        var assetList = assets.Where(a => !string.IsNullOrEmpty(a.OriginalObjectKey)).ToList();
        zip.AssetCount = assetList.Count;

        if (assetList.Count == 0)
        {
            zip.Status = ZipDownloadStatus.Failed;
            zip.ErrorMessage = "No assets found in collection";
            zip.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return assetList;
    }

    private async Task BuildAndUploadZipAsync(
        ZipDownload zip, Guid zipDownloadId, List<Asset> assetList,
        AssetHubDbContext db, CancellationToken ct)
    {
        var objectKey = $"{Constants.StoragePrefixes.TempZipDownloads}/{zipDownloadId}.zip";
        var tempPath = ScratchPaths.Combine($"assethub-zip-{zipDownloadId}.zip");

        try
        {
            var errors = await WriteZipFileAsync(tempPath, assetList, zipDownloadId, ct);
            await UploadZipFileAsync(tempPath, objectKey, ct);

            var fileInfo = new FileInfo(tempPath);
            zip.ZipObjectKey = objectKey;
            zip.SizeBytes = fileInfo.Length;
            zip.Status = ZipDownloadStatus.Completed;
            zip.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await audit.LogAsync("collection.downloaded", Constants.ScopeTypes.Collection, zip.ScopeId,
                zip.RequestedByUserId,
                new() { ["assetCount"] = zip.AssetCount, ["sizeBytes"] = zip.SizeBytes },
                ct);

            logger.LogInformation(
                "ZIP build {ZipDownloadId} completed: {AssetCount} assets, {SizeBytes} bytes ({ErrorCount} errors)",
                zipDownloadId, zip.AssetCount, zip.SizeBytes, errors.Count);
        }
        finally
        {
            CleanupTempFile(tempPath);
        }
    }

    private async Task<List<string>> WriteZipFileAsync(
        string tempPath, List<Asset> assetList, Guid zipDownloadId, CancellationToken ct)
    {
        var errors = new List<string>();

        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var asset in assetList)
        {
            ct.ThrowIfCancellationRequested();
            await TryAddAssetEntryAsync(archive, asset, errors, zipDownloadId, ct);
        }

        if (errors.Count > 0)
            await WriteErrorsEntryAsync(archive, errors);
        return errors;
    }

    private async Task TryAddAssetEntryAsync(
        ZipArchive archive, Asset asset, List<string> errors, Guid zipDownloadId, CancellationToken ct)
    {
        try
        {
            await using var assetStream = await minioAdapter.DownloadAsync(
                _bucketName, asset.OriginalObjectKey!, ct);

            var fileName = FileHelpers.GetSafeFileName(
                asset.Title ?? "untitled", asset.OriginalObjectKey!, asset.ContentType);

            var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await assetStream.CopyToAsync(entryStream, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to include asset {AssetId} in ZIP build {ZipDownloadId}",
                asset.Id, zipDownloadId);
            errors.Add($"{asset.Title ?? asset.Id.ToString()} — {ex.Message}");
        }
    }

    private static async Task WriteErrorsEntryAsync(ZipArchive archive, List<string> errors)
    {
        var errEntry = archive.CreateEntry("_errors.txt", CompressionLevel.Fastest);
        await using var errStream = errEntry.Open();
        await using var writer = new StreamWriter(errStream);
        await writer.WriteLineAsync("The following files could not be included:");
        foreach (var err in errors)
            await writer.WriteLineAsync($"  • {err}");
    }

    private async Task UploadZipFileAsync(string tempPath, string objectKey, CancellationToken ct)
    {
        await using var uploadStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await minioAdapter.UploadAsync(_bucketName, objectKey, uploadStream, "application/zip", ct);
    }

    private void CleanupTempFile(string tempPath)
    {
        if (!File.Exists(tempPath)) return;
        try { File.Delete(tempPath); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temp file {TempPath}", tempPath);
        }
    }

    private static async Task MarkFailedAsync(ZipDownload zip, string message, AssetHubDbContext db)
    {
        zip.Status = ZipDownloadStatus.Failed;
        zip.ErrorMessage = message;
        zip.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task CleanupExpiredAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var expired = await db.ZipDownloads
            .Where(z => z.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        logger.LogInformation("Cleaning up {Count} expired ZIP downloads", expired.Count);

        foreach (var objectKey in expired
            .Select(z => z.ZipObjectKey)
            .Where(key => !string.IsNullOrEmpty(key)))
        {
            try
            {
                await minioAdapter.DeleteAsync(_bucketName, objectKey!, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete ZIP object {ObjectKey}", objectKey);
            }
        }

        db.ZipDownloads.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
    }

    private static ZipDownload CreateZipDownloadRecord(
        Guid scopeId, string scopeType, string zipFileName,
        string? userId = null, string? shareTokenHash = null)
    {
        return new ZipDownload
        {
            Id = Guid.NewGuid(),
            Status = ZipDownloadStatus.Pending,
            ScopeType = scopeType.ToShareScopeType(),
            ScopeId = scopeId,
            ZipFileName = zipFileName,
            RequestedByUserId = userId,
            ShareTokenHash = shareTokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(Constants.Limits.ZipDownloadExpiryHours)
        };
    }
}
