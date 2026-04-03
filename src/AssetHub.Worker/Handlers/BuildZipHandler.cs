using AssetHub.Application.Messages;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AssetHub.Worker.Handlers;

public sealed class BuildZipHandler(
    IZipBuildService zipBuildService,
    ILogger<BuildZipHandler> logger)
{
    public async Task HandleAsync(BuildZipCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Received ZIP build command for download {ZipDownloadId}", command.ZipDownloadId);

        await zipBuildService.BuildZipAsync(command.ZipDownloadId, cancellationToken);

        logger.LogInformation("ZIP build completed for download {ZipDownloadId}", command.ZipDownloadId);
    }
}
