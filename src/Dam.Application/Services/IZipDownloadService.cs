using Dam.Domain.Entities;

namespace Dam.Application.Services;

/// <summary>
/// Streams a collection of assets as a ZIP archive directly to an output stream.
/// </summary>
public interface IZipDownloadService
{
    /// <summary>
    /// Writes a ZIP archive containing all given assets to the output stream.
    /// Sets Content-Type and Content-Disposition headers via the context callback.
    /// Logs errors for individual assets that fail and includes an _errors.txt entry in the ZIP.
    /// </summary>
    Task StreamAssetsAsZipAsync(
        IEnumerable<Asset> assets,
        string bucketName,
        string zipFileName,
        ZipStreamContext streamContext,
        CancellationToken ct = default);
}
