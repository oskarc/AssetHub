using AssetHub.Application;
using AssetHub.Application.Services.Watermarking;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services.Watermarking;

/// <summary>
/// v1 implementation of <see cref="IWatermarkEmbedder"/> — DCT-domain LSB modulation
/// with single-coefficient sign-quantisation (T5-WMK-01).
///
/// Algorithm: convert the image to Y'CbCr, divide the Y channel into 8x8 blocks,
/// forward DCT each block, modify one mid-frequency coefficient via sign-quantisation
/// to encode 1 bit per block, inverse DCT, write back, re-encode as PNG (lossless to
/// preserve the watermark). Asset and recipient layers use distinct coefficient
/// positions so they don't interfere.
///
/// Robustness target: survives JPEG re-encoding at quality 75+ and minor crops.
/// Heavy filtering / re-quantisation may strip the recipient layer; the asset layer
/// is the same algorithm but at a different coefficient — same robustness profile.
/// </summary>
public sealed class DctLsbWatermarkEmbedder(
    ILogger<DctLsbWatermarkEmbedder> logger) : IWatermarkEmbedder
{
    /// <inheritdoc />
    public string AlgorithmVersion => WatermarkConstants.AlgorithmVersions.V1;

    /// <summary>
    /// Quantisation step for the embedded coefficient. Tuned for JPEG-quality-75 survival —
    /// JPEG's luma quantisation table at q=75 has values around 8-12 in the mid-frequency
    /// region we're using, so a 16-step quantisation tolerates one quantisation pass.
    /// </summary>
    private const double QuantizationStep = 16.0;

    /// <summary>
    /// Mid-frequency coefficient for the asset-fingerprint layer. (4,3) is far enough
    /// from DC to be imperceptible and far enough from the recipient layer to avoid
    /// interference.
    /// </summary>
    private static readonly (int Y, int X) AssetCoefficient = (4, 3);

    /// <summary>Mid-frequency coefficient for the recipient-fingerprint layer.</summary>
    private static readonly (int Y, int X) RecipientCoefficient = (3, 4);

    /// <inheritdoc />
    public async Task<Stream> EmbedAssetFingerprintAsync(
        Stream input, AssetFingerprintPayload payload, CancellationToken ct)
    {
        if (!Guid.TryParse(payload.AssetWatermarkToken, out var token))
        {
            throw new ArgumentException(
                $"Asset watermark token must be a valid Guid; got '{payload.AssetWatermarkToken}'.",
                nameof(payload));
        }

        // Asset layer carries no HMAC in v1 — the 15 trailing payload bytes are zeros.
        // Forensic value is "this is asset X". Tampering with the embedded bits to
        // point to a different asset gains nothing: AssetWatermarkToken is opaque
        // and stored only in our DB; an attacker can't construct another tenant's token.
        var packed = WatermarkPayloadCodec.Pack(
            token,
            WatermarkPayloadCodec.AlgorithmVersionV1,
            ReadOnlySpan<byte>.Empty);

        var bits = WatermarkPayloadCodec.BytesToBits(packed);
        return await EmbedBitsAsync(input, bits, AssetCoefficient, "asset-fingerprint", ct);
    }

    /// <inheritdoc />
    public async Task<Stream> EmbedRecipientFingerprintAsync(
        Stream input, RecipientFingerprintPayload payload, CancellationToken ct)
    {
        // Recipient layer carries 15 bytes of HMAC pre-computed by IRecipientCryptoService.
        // Tampering with these bytes to forge another download_token is detectable at
        // verify time — HMAC won't validate.
        var packed = WatermarkPayloadCodec.Pack(
            payload.DownloadToken,
            WatermarkPayloadCodec.AlgorithmVersionV1,
            payload.PayloadHmac);

        var bits = WatermarkPayloadCodec.BytesToBits(packed);
        return await EmbedBitsAsync(input, bits, RecipientCoefficient, "recipient-fingerprint", ct);
    }

    private async Task<Stream> EmbedBitsAsync(
        Stream input, bool[] bits, (int Y, int X) coefficient, string layerName, CancellationToken ct)
    {
        if (input.CanSeek)
        {
            input.Position = 0;
        }

        using var image = new MagickImage();
        await image.ReadAsync(input, ct);

        // Switch to Y'CbCr so coefficient modifications happen in the luminance channel —
        // most JPEG-resilient and most perceptually invisible region.
        var originalColorSpace = image.ColorSpace;
        image.ColorSpace = ColorSpace.YCbCr;

        var width = (int)image.Width;
        var height = (int)image.Height;
        var blocksX = width / Dct8x8.BlockSize;
        var blocksY = height / Dct8x8.BlockSize;
        var totalBlocks = blocksX * blocksY;

        if (totalBlocks < bits.Length)
        {
            var minSide = (int)Math.Ceiling(Math.Sqrt(bits.Length)) * Dct8x8.BlockSize;
            throw new InvalidOperationException(
                $"Image too small for {layerName} watermark: need {bits.Length} 8x8 blocks " +
                $"(at least {minSide}x{minSide}px); got {totalBlocks} blocks ({width}x{height}px).");
        }

        var pixels = image.GetPixelsUnsafe();
        var block = new double[Dct8x8.BlockSize, Dct8x8.BlockSize];

        for (var bi = 0; bi < bits.Length; bi++)
        {
            ct.ThrowIfCancellationRequested();
            var by = bi / blocksX;
            var bx = bi % blocksX;
            EmbedOneBlock(pixels, block, bx, by, coefficient, bits[bi]);
        }

        image.ColorSpace = originalColorSpace;

        logger.LogDebug(
            "Embedded {LayerName} watermark — {Bits} bits across {Blocks} blocks ({Width}x{Height}px image)",
            layerName, bits.Length, totalBlocks, width, height);

        var output = new MemoryStream();
        await image.WriteAsync(output, MagickFormat.Png, ct);
        output.Position = 0;
        return output;
    }

    private static void EmbedOneBlock(
        IUnsafePixelCollection<byte> pixels, double[,] block,
        int blockX, int blockY, (int Y, int X) coefficient, bool bit)
    {
        var originX = blockX * Dct8x8.BlockSize;
        var originY = blockY * Dct8x8.BlockSize;

        // Read 8x8 Y values from channel 0.
        for (var y = 0; y < Dct8x8.BlockSize; y++)
        {
            for (var x = 0; x < Dct8x8.BlockSize; x++)
            {
                var pixel = pixels.GetPixel(originX + x, originY + y);
                block[y, x] = pixel.GetChannel(0);
            }
        }

        Dct8x8.Forward(block);

        // Sign-quantisation parity flip on the chosen coefficient.
        var c = block[coefficient.Y, coefficient.X];
        var quantized = (int)Math.Round(c / QuantizationStep);
        var currentBit = (Math.Abs(quantized) & 1) == 1;
        if (currentBit != bit)
        {
            // Flip parity by nudging away from zero. Adjusting toward zero risks crossing
            // sign boundary and changing the absolute-value semantics.
            quantized += quantized >= 0 ? 1 : -1;
        }
        block[coefficient.Y, coefficient.X] = quantized * QuantizationStep;

        Dct8x8.Inverse(block);

        // Write back the modified Y values, clamping to byte range.
        for (var y = 0; y < Dct8x8.BlockSize; y++)
        {
            for (var x = 0; x < Dct8x8.BlockSize; x++)
            {
                var newY = (byte)Math.Clamp(Math.Round(block[y, x]), 0, 255);
                var pixel = pixels.GetPixel(originX + x, originY + y);
                pixel.SetChannel(0, newY);
            }
        }
    }
}
