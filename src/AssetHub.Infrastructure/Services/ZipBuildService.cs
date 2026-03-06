using System.IO.Compression;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Groups data-access dependencies for <see cref="ZipBuildService"/>.
/// </summary>
public sealed record ZipBuildDataDependencies(
    IDbContextFactory<AssetHubDbContext> DbFactory,
    IAssetRepository AssetRepo,
    ICollectionRepository CollectionRepo);

/// <summary>
/// Manages queued ZIP download builds via Hangfire.
/// Builds ZIP files in the background and stores them as temporary MinIO objects.
/// </summary>
public class ZipBuildService : IZipBuildService
{
    private readonly IDbContextFactory<AssetHubDbContext> _dbFactory;
    private readonly IAssetRepository _assetRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly IMinIOAdapter _minioAdapter;
    private readonly IBackgroundJobClient _jobClient;
    private readonly IAuditService _audit;
    private readonly string _bucketName;
    private readonly ILogger<ZipBuildService> _logger;

    public ZipBuildService(
        ZipBuildDataDependencies data,
        IMinIOAdapter minioAdapter,
        IBackgroundJobClient jobClient,
        IAuditService audit,
        IOptions<MinIOSettings> minioSettings,
        ILogger<ZipBuildService> logger)
    {
        _dbFactory = data.DbFactory;
        _assetRepo = data.AssetRepo;
        _collectionRepo = data.CollectionRepo;
        _minioAdapter = minioAdapter;
        _jobClient = jobClient;
        _audit = audit;
        _bucketName = minioSettings.Value.BucketName;
        _logger = logger;
    }


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
        if (collection == null)
            return ServiceError.NotFound("Collection not found");

        var zipFileName = $"{collection.Name.Replace(" ", "_")}_assets.zip";
        var zipDownload = CreateZipDownloadRecord(collectionId, Constants.ScopeTypes.Collection, zipFileName, userId: userId);

        db.ZipDownloads.Add(zipDownload);
        await db.SaveChangesAsync(ct);

        var hangfireJobId = _jobClient.Enqueue<ZipBuildService>(svc => svc.BuildZipAsync(zipDownload.Id, CancellationToken.None));
        zipDownload.HangfireJobId = hangfireJobId;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Enqueued ZIP build {ZipDownloadId} for collection {CollectionId} by user {UserId}",
            zipDownload.Id, collectionId, userId);

        return new ZipDownloadEnqueuedResponse
        {
            JobId = zipDownload.Id,
            StatusUrl = $"/api/zip-downloads/{zipDownload.Id}",
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

        var hangfireJobId = _jobClient.Enqueue<ZipBuildService>(svc => svc.BuildZipAsync(zipDownload.Id, CancellationToken.None));
        zipDownload.HangfireJobId = hangfireJobId;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Enqueued ZIP build {ZipDownloadId} for shared collection {CollectionId}",
            zipDownload.Id, collectionId);

        return new ZipDownloadEnqueuedResponse
        {
            JobId = zipDownload.Id,
            StatusUrl = $"/api/zip-downloads/{zipDownload.Id}/share",
            Message = "ZIP download queued. Poll the status URL for progress."
        };
    }

    public async Task<ServiceResult<ZipDownloadStatusResponse>> GetStatusAsync(
        Guid jobId, string? userId, string? shareTokenHash, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var zip = await db.ZipDownloads.FirstOrDefaultAsync(z => z.Id == jobId, ct);
        if (zip == null)
            return ServiceError.NotFound("ZIP download not found");

        // Ensure the requester owns this download
        if (userId != null && zip.RequestedByUserId != userId)
            return ServiceError.Forbidden();
        if (shareTokenHash != null && zip.ShareTokenHash != shareTokenHash)
            return ServiceError.Forbidden();
        if (userId == null && shareTokenHash == null)
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

            var downloadUrl = await _minioAdapter.GetPresignedDownloadUrlAsync(
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

    [AutomaticRetry(Attempts = 1)]
    public async Task BuildZipAsync(Guid zipDownloadId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var zip = await db.ZipDownloads.FirstOrDefaultAsync(z => z.Id == zipDownloadId, ct);
        if (zip == null)
        {
            _logger.LogWarning("ZIP download record {ZipDownloadId} not found, skipping", zipDownloadId);
            return;
        }

        zip.Status = ZipDownloadStatus.Building;
        await db.SaveChangesAsync(ct);

        try
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
                return;
            }

            // Build the ZIP into a temp file, then upload to MinIO
            var objectKey = $"{Constants.StoragePrefixes.TempZipDownloads}/{zipDownloadId}.zip";
            var tempPath = Path.Combine(Path.GetTempPath(), $"assethub-zip-{zipDownloadId}.zip");

            try
            {
                var errors = new List<string>();

                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (var asset in assetList)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            await using var assetStream = await _minioAdapter.DownloadAsync(
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
                            _logger.LogWarning(ex,
                                "Failed to include asset {AssetId} in ZIP build {ZipDownloadId}",
                                asset.Id, zipDownloadId);
                            errors.Add($"{asset.Title ?? asset.Id.ToString()} — {ex.Message}");
                        }
                    }

                    // Add error log if any assets failed
                    if (errors.Count > 0)
                    {
                        var errEntry = archive.CreateEntry("_errors.txt", CompressionLevel.Fastest);
                        await using var errStream = errEntry.Open();
                        await using var writer = new StreamWriter(errStream);
                        await writer.WriteLineAsync("The following files could not be included:");
                        foreach (var err in errors)
                            await writer.WriteLineAsync($"  • {err}");
                    }
                }

                // Upload the completed ZIP to MinIO
                var fileInfo = new FileInfo(tempPath);
                await using (var uploadStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await _minioAdapter.UploadAsync(_bucketName, objectKey, uploadStream, "application/zip", ct);
                }

                zip.ZipObjectKey = objectKey;
                zip.SizeBytes = fileInfo.Length;
                zip.Status = ZipDownloadStatus.Completed;
                zip.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                await _audit.LogAsync("collection.downloaded", Constants.ScopeTypes.Collection, zip.ScopeId,
                    zip.RequestedByUserId,
                    new() { ["assetCount"] = zip.AssetCount, ["sizeBytes"] = zip.SizeBytes },
                    ct);

                _logger.LogInformation(
                    "ZIP build {ZipDownloadId} completed: {AssetCount} assets, {SizeBytes} bytes",
                    zipDownloadId, zip.AssetCount, zip.SizeBytes);
            }
            finally
            {
                // Always clean up the temp file
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temp file {TempPath}", tempPath);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            zip.Status = ZipDownloadStatus.Failed;
            zip.ErrorMessage = "Build was cancelled";
            zip.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZIP build {ZipDownloadId} failed", zipDownloadId);
            zip.Status = ZipDownloadStatus.Failed;
            zip.ErrorMessage = "An error occurred while building the ZIP file";
            zip.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task CleanupExpiredAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var expired = await db.ZipDownloads
            .Where(z => z.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        _logger.LogInformation("Cleaning up {Count} expired ZIP downloads", expired.Count);

        foreach (var objectKey in expired
            .Select(z => z.ZipObjectKey)
            .Where(key => !string.IsNullOrEmpty(key)))
        {
            try
            {
                await _minioAdapter.DeleteAsync(_bucketName, objectKey, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete ZIP object {ObjectKey}", objectKey);
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
