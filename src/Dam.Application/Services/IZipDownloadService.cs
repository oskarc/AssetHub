using Dam.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace Dam.Application.Services;

/// <summary>
/// Streams a collection of assets as a ZIP archive directly to the HTTP response.
/// </summary>
public interface IZipDownloadService
{
    /// <summary>
    /// Writes a ZIP archive containing all given assets to the HTTP response body.
    /// Sets Content-Type and Content-Disposition headers. Logs errors for individual
    /// assets that fail and includes an _errors.txt entry in the ZIP if any occur.
    /// </summary>
    Task StreamAssetsAsZipAsync(
        IEnumerable<Asset> assets,
        string bucketName,
        string zipFileName,
        HttpContext httpContext,
        CancellationToken ct = default);
}
