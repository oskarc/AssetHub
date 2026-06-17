using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task SetCollectionWatermarkAsync(
        Guid collectionId, bool enabled, CancellationToken ct = default)
    {
        var result = await watermarkService.SetCollectionWatermarkAsync(collectionId, enabled, ct);
        EnsureSuccess(result, "Toggle collection watermarking");
    }

    public async Task SetAssetWatermarkOverrideAsync(
        Guid assetId, bool? @override, CancellationToken ct = default)
    {
        var result = await watermarkService.SetAssetWatermarkOverrideAsync(assetId, @override, ct);
        EnsureSuccess(result, "Set asset watermark override");
    }

    public async Task SetShareWatermarkOverrideAsync(
        Guid shareId, bool? @override, CancellationToken ct = default)
    {
        var result = await watermarkService.SetShareWatermarkOverrideAsync(shareId, @override, ct);
        EnsureSuccess(result, "Set share watermark override");
    }

    public async Task<WatermarkVerificationResultDto> VerifyWatermarkAsync(
        Stream content, string contentType, long sizeBytes, CancellationToken ct = default)
    {
        var result = await watermarkVerifier.VerifyAsync(content, contentType, sizeBytes, ct);
        return Unwrap(result, "Verify watermark");
    }
}
