using System.IO.Compression;
using Dam.Application.Helpers;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Dam.Infrastructure.Services;

/// <inheritdoc />
public class ZipDownloadService(
    IMinIOAdapter minioAdapter,
    ILogger<ZipDownloadService> logger) : IZipDownloadService
{
    public async Task StreamAssetsAsZipAsync(
        IEnumerable<Asset> assets,
        string bucketName,
        string zipFileName,
        ZipStreamContext streamContext,
        CancellationToken ct = default)
    {
        streamContext.SetHeader("Content-Type", "application/zip");
        streamContext.SetHeader("Content-Disposition", $"attachment; filename=\"{zipFileName}\"");

        var errors = new List<string>();
        // Do NOT dispose the output stream — it is owned by the HTTP response pipeline.
        var responseStream = streamContext.OutputStream;
        using var archive = new ZipArchive(responseStream, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var asset in assets.Where(a => !string.IsNullOrEmpty(a.OriginalObjectKey)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var assetStream = await minioAdapter.DownloadAsync(bucketName, asset.OriginalObjectKey!, ct);
                var fileName = FileHelpers.GetSafeFileName(asset.Title ?? "untitled", asset.OriginalObjectKey!, asset.ContentType);

                var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await assetStream.CopyToAsync(entryStream, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to include asset {AssetId} ({ObjectKey}) in ZIP download",
                    asset.Id, asset.OriginalObjectKey);
                errors.Add($"{asset.Title ?? asset.Id.ToString()} — {ex.Message}");
            }
        }

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
}
