namespace AssetHub.Infrastructure.Services.Watermarking;

/// <summary>
/// Packs the watermark payload (token + algorithm version + HMAC) into a fixed
/// 32-byte / 256-bit buffer (T5-WMK-01), and converts byte buffers to / from the
/// bit arrays the embedder writes one-bit-per-block into the DCT coefficients.
///
/// Layout (32 bytes / 256 bits):
/// <list type="bullet">
///   <item>Bytes  0..15 — 128-bit token (Guid)</item>
///   <item>Byte  16     — algorithm version (e.g. 0x01 for v1)</item>
///   <item>Bytes 17..31 — 15 bytes of HMAC / checksum (truncated SHA-256 HMAC for the recipient layer; deterministic checksum for the asset layer)</item>
/// </list>
/// </summary>
internal static class WatermarkPayloadCodec
{
    public const int PayloadByteSize = 32;
    public const int PayloadBitSize = PayloadByteSize * 8;

    /// <summary>Algorithm version v1: DCT-domain LSB modulation, single-coefficient sign-quantisation.</summary>
    public const byte AlgorithmVersionV1 = 0x01;

    /// <summary>Pack <paramref name="token"/> + <paramref name="algorithmVersion"/> + <paramref name="hmac"/> into 32 bytes. Truncates / pads <paramref name="hmac"/> to 15 bytes as needed.</summary>
    public static byte[] Pack(Guid token, byte algorithmVersion, ReadOnlySpan<byte> hmac)
    {
        var buf = new byte[PayloadByteSize];
        if (!token.TryWriteBytes(buf.AsSpan(0, 16)))
        {
            throw new InvalidOperationException("Guid.TryWriteBytes failed for a 16-byte buffer.");
        }
        buf[16] = algorithmVersion;
        var hmacLen = Math.Min(hmac.Length, 15);
        hmac[..hmacLen].CopyTo(buf.AsSpan(17, hmacLen));
        return buf;
    }

    /// <summary>Unpack a 32-byte payload back into its three components. Throws if <paramref name="payload"/> is shorter than 32 bytes.</summary>
    public static (Guid Token, byte AlgorithmVersion, byte[] Hmac) Unpack(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < PayloadByteSize)
        {
            throw new ArgumentException(
                $"Payload must be at least {PayloadByteSize} bytes, got {payload.Length}.",
                nameof(payload));
        }

        var token = new Guid(payload[..16]);
        var algorithmVersion = payload[16];
        var hmac = payload.Slice(17, 15).ToArray();
        return (token, algorithmVersion, hmac);
    }

    /// <summary>Convert a byte buffer to a bit array. LSB-first within each byte (bit 0 of byte 0 is bits[0], bit 7 of byte 0 is bits[7], etc.).</summary>
    public static bool[] BytesToBits(ReadOnlySpan<byte> bytes)
    {
        var bits = new bool[bytes.Length * 8];
        for (var i = 0; i < bytes.Length; i++)
        {
            for (var b = 0; b < 8; b++)
            {
                bits[(i * 8) + b] = ((bytes[i] >> b) & 1) == 1;
            }
        }
        return bits;
    }

    /// <summary>Convert a bit array back to bytes. Length must be a multiple of 8. LSB-first matching <see cref="BytesToBits"/>.</summary>
    public static byte[] BitsToBytes(ReadOnlySpan<bool> bits)
    {
        if (bits.Length % 8 != 0)
        {
            throw new ArgumentException(
                $"Bit count must be a multiple of 8, got {bits.Length}.",
                nameof(bits));
        }

        var bytes = new byte[bits.Length / 8];
        for (var i = 0; i < bytes.Length; i++)
        {
            byte b = 0;
            for (var j = 0; j < 8; j++)
            {
                if (bits[(i * 8) + j]) b |= (byte)(1 << j);
            }
            bytes[i] = b;
        }
        return bytes;
    }
}
