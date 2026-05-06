namespace AssetHub.Infrastructure.Services.Watermarking;

/// <summary>
/// Forward and inverse 2D DCT-II on 8x8 blocks (T5-WMK-01). Implemented as
/// separable 1D DCTs across rows then columns — same shape JPEG uses.
/// Orthonormal scaling so a forward followed by an inverse round-trips
/// (modulo floating-point error).
/// </summary>
/// <remarks>
/// Cosine and normalisation tables are precomputed at type initialisation.
/// All operations are in-place on a <c>double[8,8]</c>.
/// </remarks>
internal static class Dct8x8
{
    public const int BlockSize = 8;

    // Cosine[k, n] = cos((2n+1) * k * PI / 16). Precomputed once; read-only thereafter.
    private static readonly double[,] Cosine = ComputeCosine();

    // Orthonormal scaling: Norm[0] = 1/sqrt(8); Norm[k>0] = sqrt(2/8) = 1/2.
    private static readonly double[] Norm = ComputeNorm();

    private static double[,] ComputeCosine()
    {
        var m = new double[BlockSize, BlockSize];
        for (var k = 0; k < BlockSize; k++)
        {
            for (var n = 0; n < BlockSize; n++)
            {
                m[k, n] = Math.Cos((2 * n + 1) * k * Math.PI / 16.0);
            }
        }
        return m;
    }

    private static double[] ComputeNorm()
    {
        var n = new double[BlockSize];
        n[0] = 1.0 / Math.Sqrt(BlockSize);
        for (var k = 1; k < BlockSize; k++)
        {
            n[k] = 0.5; // sqrt(2/8)
        }
        return n;
    }

    /// <summary>Forward 2D DCT-II on an 8x8 block. In-place.</summary>
    public static void Forward(double[,] block)
    {
        Span<double> row = stackalloc double[BlockSize];
        Span<double> rowOut = stackalloc double[BlockSize];

        // 1D DCT-II on each row.
        for (var y = 0; y < BlockSize; y++)
        {
            for (var x = 0; x < BlockSize; x++) row[x] = block[y, x];
            Forward1D(row, rowOut);
            for (var x = 0; x < BlockSize; x++) block[y, x] = rowOut[x];
        }

        // 1D DCT-II on each column.
        Span<double> col = stackalloc double[BlockSize];
        Span<double> colOut = stackalloc double[BlockSize];
        for (var x = 0; x < BlockSize; x++)
        {
            for (var y = 0; y < BlockSize; y++) col[y] = block[y, x];
            Forward1D(col, colOut);
            for (var y = 0; y < BlockSize; y++) block[y, x] = colOut[y];
        }
    }

    /// <summary>Inverse 2D DCT-II on an 8x8 block. In-place.</summary>
    public static void Inverse(double[,] block)
    {
        Span<double> row = stackalloc double[BlockSize];
        Span<double> rowOut = stackalloc double[BlockSize];
        for (var y = 0; y < BlockSize; y++)
        {
            for (var x = 0; x < BlockSize; x++) row[x] = block[y, x];
            Inverse1D(row, rowOut);
            for (var x = 0; x < BlockSize; x++) block[y, x] = rowOut[x];
        }

        Span<double> col = stackalloc double[BlockSize];
        Span<double> colOut = stackalloc double[BlockSize];
        for (var x = 0; x < BlockSize; x++)
        {
            for (var y = 0; y < BlockSize; y++) col[y] = block[y, x];
            Inverse1D(col, colOut);
            for (var y = 0; y < BlockSize; y++) block[y, x] = colOut[y];
        }
    }

    private static void Forward1D(ReadOnlySpan<double> input, Span<double> output)
    {
        for (var k = 0; k < BlockSize; k++)
        {
            var sum = 0.0;
            for (var n = 0; n < BlockSize; n++) sum += input[n] * Cosine[k, n];
            output[k] = sum * Norm[k];
        }
    }

    private static void Inverse1D(ReadOnlySpan<double> input, Span<double> output)
    {
        for (var n = 0; n < BlockSize; n++)
        {
            var sum = 0.0;
            for (var k = 0; k < BlockSize; k++) sum += input[k] * Cosine[k, n] * Norm[k];
            output[n] = sum;
        }
    }
}
