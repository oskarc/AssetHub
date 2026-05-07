using AssetHub.Application.Services.Watermarking;
using AssetHub.Infrastructure.Services.Watermarking;
using ImageMagick;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Services;

/// <summary>
/// Round-trip tests for <see cref="DctLsbWatermarkEmbedder"/> +
/// <see cref="DctLsbWatermarkExtractor"/> (T5-WMK-01 G-45).
///
/// Validates the no-gap rule's foundation: if both layers are embedded into a stream
/// (asset-fingerprint inline + recipient-fingerprint per-download), the extractor
/// must recover both. This is the core invariant that makes "first download before
/// the background sweep" still attributable.
/// </summary>
public class DctLsbWatermarkRoundTripTests
{
    private readonly DctLsbWatermarkEmbedder _embedder = new(NullLogger<DctLsbWatermarkEmbedder>.Instance);
    private readonly DctLsbWatermarkExtractor _extractor = new(NullLogger<DctLsbWatermarkExtractor>.Instance);

    /// <summary>
    /// 256x256 grey-noise PNG. Big enough to fit 256-bit payloads (16x16 8-blocks min);
    /// noise rather than solid-fill so DCT coefficients aren't all zero (which would
    /// quantise unstably around the embedding threshold).
    /// </summary>
    private static MemoryStream CreateTestImage(int size = 256)
    {
        using var image = new MagickImage(MagickColors.Gray, (uint)size, (uint)size);
        // Add deterministic noise so DCT mid-frequencies have real content to modulate.
        var rng = new Random(42);
        var pixels = image.GetPixelsUnsafe();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var v = (byte)(128 + rng.Next(-32, 32));
                pixels.SetPixel(x, y, [v, v, v]);
            }
        }
        var stream = new MemoryStream();
        image.Write(stream, MagickFormat.Png);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task EmbedAndExtract_AssetLayerOnly_RecoversToken()
    {
        var input = CreateTestImage();
        var token = Guid.NewGuid();
        var payload = new AssetFingerprintPayload(token.ToString("D"), _embedder.AlgorithmVersion);

        await using var watermarked = await _embedder.EmbedAssetFingerprintAsync(input, payload, CancellationToken.None);
        var result = await _extractor.ExtractAsync(watermarked, CancellationToken.None);

        Assert.NotNull(result.Asset.Token);
        Assert.Equal(token.ToString("D"), result.Asset.Token);
        Assert.NotEqual(WatermarkLayerConfidence.NoMatch, result.Asset.Confidence);
    }

    [Fact]
    public async Task EmbedAndExtract_BothLayers_RecoversBoth()
    {
        // No-gap rule: when stored renditions don't yet carry the asset fingerprint,
        // the on-the-fly path embeds both. Both must extract back from the same bytes.
        var input = CreateTestImage();
        var assetToken = Guid.NewGuid();
        var downloadToken = Guid.NewGuid();
        var hmac = new byte[15];
        new Random(1).NextBytes(hmac);

        var assetPayload = new AssetFingerprintPayload(assetToken.ToString("D"), _embedder.AlgorithmVersion);
        var recipientPayload = new RecipientFingerprintPayload(downloadToken, _embedder.AlgorithmVersion, hmac);

        await using var afterAsset = await _embedder.EmbedAssetFingerprintAsync(input, assetPayload, CancellationToken.None);
        await using var bothLayers = await _embedder.EmbedRecipientFingerprintAsync(afterAsset, recipientPayload, CancellationToken.None);

        var result = await _extractor.ExtractAsync(bothLayers, CancellationToken.None);

        Assert.Equal(assetToken.ToString("D"), result.Asset.Token);
        Assert.NotEqual(WatermarkLayerConfidence.NoMatch, result.Asset.Confidence);
        Assert.Equal(downloadToken, result.Recipient.Token);
        Assert.NotEqual(WatermarkLayerConfidence.NoMatch, result.Recipient.Confidence);
        Assert.NotNull(result.Recipient.RawPayload);
    }

    [Fact]
    public async Task Extract_UnwatermarkedImage_ReportsNoMatch()
    {
        var input = CreateTestImage();

        var result = await _extractor.ExtractAsync(input, CancellationToken.None);

        Assert.Equal(WatermarkLayerConfidence.NoMatch, result.Asset.Confidence);
        Assert.Equal(WatermarkLayerConfidence.NoMatch, result.Recipient.Confidence);
    }

    [Fact]
    public async Task Embed_ImageTooSmall_Throws()
    {
        // Algorithm needs 256 8x8 blocks (16x16 grid); 64x64 is only 8x8 = 64 blocks.
        var input = CreateTestImage(64);
        var payload = new AssetFingerprintPayload(Guid.NewGuid().ToString("D"), _embedder.AlgorithmVersion);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _embedder.EmbedAssetFingerprintAsync(input, payload, CancellationToken.None));
    }

    [Fact]
    public async Task EmbedAssetLayer_InvalidGuid_Throws()
    {
        var input = CreateTestImage();
        var payload = new AssetFingerprintPayload("not-a-guid", _embedder.AlgorithmVersion);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _embedder.EmbedAssetFingerprintAsync(input, payload, CancellationToken.None));
    }
}
