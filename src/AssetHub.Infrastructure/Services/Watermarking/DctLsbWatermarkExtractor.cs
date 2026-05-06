using AssetHub.Application;
using AssetHub.Application.Services.Watermarking;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services.Watermarking;

/// <summary>
/// v1 implementation of <see cref="IWatermarkExtractor"/> — symmetric to
/// <see cref="DctLsbWatermarkEmbedder"/> (T5-WMK-01). Reads 8x8 Y'CbCr blocks,
/// forward DCT, samples the parity of the chosen coefficient, accumulates 256 bits
/// per layer in one pass, and decodes both payloads.
///
/// Confidence is format-level only — bits decode to a non-empty token + matching
/// algorithm version → <see cref="WatermarkLayerConfidence.Medium"/>.
/// HMAC verification (recipient layer) and asset-row resolution (asset layer)
/// happen in <see cref="IWatermarkVerifier"/>; that's where Medium can be promoted
/// to <see cref="WatermarkLayerConfidence.High"/> or demoted to NoMatch.
/// </summary>
public sealed class DctLsbWatermarkExtractor(
    ILogger<DctLsbWatermarkExtractor> logger) : IWatermarkExtractor
{
    /// <inheritdoc />
    public string AlgorithmVersion => WatermarkConstants.AlgorithmVersions.V1;

    private const double QuantizationStep = 16.0;
    private static readonly (int Y, int X) AssetCoefficient = (4, 3);
    private static readonly (int Y, int X) RecipientCoefficient = (3, 4);

    /// <inheritdoc />
    public async Task<WatermarkExtractionResult> ExtractAsync(Stream input, CancellationToken ct)
    {
        if (input.CanSeek)
        {
            input.Position = 0;
        }

        using var image = new MagickImage();
        await image.ReadAsync(input, ct);
        image.ColorSpace = ColorSpace.YCbCr;

        var width = (int)image.Width;
        var height = (int)image.Height;
        var blocksX = width / Dct8x8.BlockSize;
        var blocksY = height / Dct8x8.BlockSize;
        var totalBlocks = blocksX * blocksY;

        if (totalBlocks < WatermarkPayloadCodec.PayloadBitSize)
        {
            // Image too small to carry watermark; both layers no-match.
            logger.LogDebug(
                "Image {Width}x{Height} too small for watermark extraction (needs {Required} blocks, got {Actual})",
                width, height, WatermarkPayloadCodec.PayloadBitSize, totalBlocks);
            return NoMatchResult();
        }

        var pixels = image.GetPixelsUnsafe();
        var assetBits = new bool[WatermarkPayloadCodec.PayloadBitSize];
        var recipientBits = new bool[WatermarkPayloadCodec.PayloadBitSize];
        var block = new double[Dct8x8.BlockSize, Dct8x8.BlockSize];

        // Single pass — extract both layers from each of the first 256 blocks.
        for (var bi = 0; bi < WatermarkPayloadCodec.PayloadBitSize; bi++)
        {
            ct.ThrowIfCancellationRequested();
            var by = bi / blocksX;
            var bx = bi % blocksX;
            ExtractOneBlock(pixels, block, bx, by, assetBits, recipientBits, bi);
        }

        var asset = DecodeAssetLayer(assetBits);
        var recipient = DecodeRecipientLayer(recipientBits);

        return new WatermarkExtractionResult(asset, recipient);
    }

    private static WatermarkExtractionResult NoMatchResult()
        => new(
            new AssetLayerExtraction(Token: null, RawPayload: null, Confidence: WatermarkLayerConfidence.NoMatch),
            new RecipientLayerExtraction(Token: null, RawPayload: null, Confidence: WatermarkLayerConfidence.NoMatch));

    private static void ExtractOneBlock(
        IUnsafePixelCollection<byte> pixels, double[,] block,
        int blockX, int blockY, bool[] assetBits, bool[] recipientBits, int bi)
    {
        var originX = blockX * Dct8x8.BlockSize;
        var originY = blockY * Dct8x8.BlockSize;

        for (var y = 0; y < Dct8x8.BlockSize; y++)
        {
            for (var x = 0; x < Dct8x8.BlockSize; x++)
            {
                var pixel = pixels.GetPixel(originX + x, originY + y);
                block[y, x] = pixel.GetChannel(0);
            }
        }

        Dct8x8.Forward(block);

        assetBits[bi] = ReadBitParity(block[AssetCoefficient.Y, AssetCoefficient.X]);
        recipientBits[bi] = ReadBitParity(block[RecipientCoefficient.Y, RecipientCoefficient.X]);
    }

    private static bool ReadBitParity(double coefficient)
    {
        var quantized = (int)Math.Round(coefficient / QuantizationStep);
        return (Math.Abs(quantized) & 1) == 1;
    }

    private static AssetLayerExtraction DecodeAssetLayer(bool[] bits)
    {
        var bytes = WatermarkPayloadCodec.BitsToBytes(bits);
        var (token, version, _) = WatermarkPayloadCodec.Unpack(bytes);

        if (version != WatermarkPayloadCodec.AlgorithmVersionV1 || token == Guid.Empty)
        {
            return new AssetLayerExtraction(Token: null, RawPayload: null, Confidence: WatermarkLayerConfidence.NoMatch);
        }

        // Format-level extraction succeeded. Verifier looks up Asset.AssetWatermarkToken
        // to upgrade Medium → High (or NoMatch if no asset matches).
        return new AssetLayerExtraction(
            Token: token.ToString("D"),
            RawPayload: bytes,
            Confidence: WatermarkLayerConfidence.Medium);
    }

    private static RecipientLayerExtraction DecodeRecipientLayer(bool[] bits)
    {
        var bytes = WatermarkPayloadCodec.BitsToBytes(bits);
        var (token, version, _) = WatermarkPayloadCodec.Unpack(bytes);

        if (version != WatermarkPayloadCodec.AlgorithmVersionV1 || token == Guid.Empty)
        {
            return new RecipientLayerExtraction(Token: null, RawPayload: null, Confidence: WatermarkLayerConfidence.NoMatch);
        }

        // Format-level extraction succeeded. Verifier HMAC-checks the embedded payload
        // bytes against IRecipientCryptoService to upgrade Medium → High.
        return new RecipientLayerExtraction(
            Token: token,
            RawPayload: bytes,
            Confidence: WatermarkLayerConfidence.Medium);
    }
}
