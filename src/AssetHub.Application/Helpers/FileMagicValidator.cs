using System.Collections.Frozen;

namespace AssetHub.Application.Helpers;

/// <summary>
/// Validates file content against known magic byte signatures to prevent
/// Content-Type spoofing attacks. This provides server-side verification
/// that file content matches the claimed MIME type.
/// </summary>
public static class FileMagicValidator
{
    /// <summary>
    /// File signature definition with magic bytes and optional offset.
    /// </summary>
    private readonly record struct Signature(byte[] Bytes, int Offset = 0);

    /// <summary>
    /// Maps MIME type prefixes or exact types to their known file signatures.
    /// Multiple signatures per type handle format variations (e.g., JPEG has multiple markers).
    /// </summary>
    private static readonly FrozenDictionary<string, Signature[]> KnownSignatures = new Dictionary<string, Signature[]>
    {
        // Images
        ["image/jpeg"] =
        [
            new([0xFF, 0xD8, 0xFF]),          // JPEG/JFIF/EXIF
        ],
        ["image/png"] =
        [
            new([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]), // PNG
        ],
        ["image/gif"] =
        [
            new([0x47, 0x49, 0x46, 0x38, 0x37, 0x61]), // GIF87a
            new([0x47, 0x49, 0x46, 0x38, 0x39, 0x61]), // GIF89a
        ],
        ["image/webp"] =
        [
            new([0x52, 0x49, 0x46, 0x46]),    // RIFF header (WebP is RIFF-based)
            // Note: Full WebP validation would check for "WEBP" at offset 8
        ],
        ["image/bmp"] =
        [
            new([0x42, 0x4D]),                // BM
        ],
        ["image/tiff"] =
        [
            new([0x49, 0x49, 0x2A, 0x00]),    // Little-endian TIFF
            new([0x4D, 0x4D, 0x00, 0x2A]),    // Big-endian TIFF
        ],
        ["image/x-icon"] =
        [
            new([0x00, 0x00, 0x01, 0x00]),    // ICO
        ],
        ["image/vnd.microsoft.icon"] =
        [
            new([0x00, 0x00, 0x01, 0x00]),    // ICO
        ],
        ["image/heic"] =
        [
            new([0x66, 0x74, 0x79, 0x70], Offset: 4), // "ftyp" at offset 4
        ],
        ["image/heif"] =
        [
            new([0x66, 0x74, 0x79, 0x70], Offset: 4), // "ftyp" at offset 4
        ],
        ["image/avif"] =
        [
            new([0x66, 0x74, 0x79, 0x70], Offset: 4), // "ftyp" at offset 4
        ],

        // Video formats
        ["video/mp4"] =
        [
            new([0x66, 0x74, 0x79, 0x70], Offset: 4), // "ftyp" at offset 4
        ],
        ["video/quicktime"] =
        [
            new([0x66, 0x74, 0x79, 0x70], Offset: 4), // "ftyp" at offset 4 (MOV)
        ],
        ["video/x-msvideo"] =
        [
            new([0x52, 0x49, 0x46, 0x46]),    // RIFF (AVI)
        ],
        ["video/x-matroska"] =
        [
            new([0x1A, 0x45, 0xDF, 0xA3]),    // EBML header (MKV/WebM)
        ],
        ["video/webm"] =
        [
            new([0x1A, 0x45, 0xDF, 0xA3]),    // EBML header
        ],
        ["video/mpeg"] =
        [
            new([0x00, 0x00, 0x01, 0xBA]),    // MPEG Program Stream
            new([0x00, 0x00, 0x01, 0xB3]),    // MPEG video elementary stream
        ],
        ["video/ogg"] =
        [
            new([0x4F, 0x67, 0x67, 0x53]),    // OggS
        ],

        // Audio formats
        ["audio/mpeg"] =
        [
            new([0xFF, 0xFB]),                // MP3 frame sync (layer 3)
            new([0xFF, 0xFA]),                // MP3 frame sync
            new([0xFF, 0xF3]),                // MP3 frame sync
            new([0xFF, 0xF2]),                // MP3 frame sync
            new([0x49, 0x44, 0x33]),          // ID3 tag (MP3 with metadata)
        ],
        ["audio/wav"] =
        [
            new([0x52, 0x49, 0x46, 0x46]),    // RIFF (WAV)
        ],
        ["audio/x-wav"] =
        [
            new([0x52, 0x49, 0x46, 0x46]),
        ],
        ["audio/ogg"] =
        [
            new([0x4F, 0x67, 0x67, 0x53]),    // OggS
        ],
        ["audio/flac"] =
        [
            new([0x66, 0x4C, 0x61, 0x43]),    // fLaC
        ],
        ["audio/aac"] =
        [
            new([0xFF, 0xF1]),                // ADTS AAC
            new([0xFF, 0xF9]),                // ADTS AAC
        ],
        ["audio/mp4"] =
        [
            new([0x66, 0x74, 0x79, 0x70], Offset: 4), // "ftyp" at offset 4 (M4A)
        ],
        ["audio/x-m4a"] =
        [
            new([0x66, 0x74, 0x79, 0x70], Offset: 4), // "ftyp" at offset 4
        ],

        // Documents
        ["application/pdf"] =
        [
            new([0x25, 0x50, 0x44, 0x46]),    // %PDF
        ],
        ["application/zip"] =
        [
            new([0x50, 0x4B, 0x03, 0x04]),    // PK (ZIP)
            new([0x50, 0x4B, 0x05, 0x06]),    // Empty ZIP
            new([0x50, 0x4B, 0x07, 0x08]),    // Spanned ZIP
        ],
        ["application/x-zip-compressed"] =
        [
            new([0x50, 0x4B, 0x03, 0x04]),
        ],
        // OOXML formats (docx, xlsx, pptx) are ZIP-based
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] =
        [
            new([0x50, 0x4B, 0x03, 0x04]),    // ZIP
        ],
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] =
        [
            new([0x50, 0x4B, 0x03, 0x04]),
        ],
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] =
        [
            new([0x50, 0x4B, 0x03, 0x04]),
        ],
        // Legacy Office formats
        ["application/msword"] =
        [
            new([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]), // OLE Compound File
        ],
        ["application/vnd.ms-excel"] =
        [
            new([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
        ],
        ["application/vnd.ms-powerpoint"] =
        [
            new([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
        ],

        // Fonts
        ["font/woff"] =
        [
            new([0x77, 0x4F, 0x46, 0x46]),    // wOFF
        ],
        ["font/woff2"] =
        [
            new([0x77, 0x4F, 0x46, 0x32]),    // wOF2
        ],
        ["font/ttf"] =
        [
            new([0x00, 0x01, 0x00, 0x00]),    // TrueType
        ],
        ["font/otf"] =
        [
            new([0x4F, 0x54, 0x54, 0x4F]),    // OTTO (OpenType)
        ],
        ["application/x-font-woff"] =
        [
            new([0x77, 0x4F, 0x46, 0x46]),
        ],

        // 3D models
        ["model/gltf-binary"] =
        [
            new([0x67, 0x6C, 0x54, 0x46]),    // glTF
        ],
    }.ToFrozenDictionary();

    /// <summary>
    /// MIME types that are inherently text-based and cannot be validated via magic bytes.
    /// These types are allowed without signature validation.
    /// </summary>
    private static readonly FrozenSet<string> TextBasedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/csv",
        "application/json",
        "application/xml",
        "text/xml",
        "application/svg+xml",  // Note: SVG is disabled in ImageMagick policy
        "model/gltf+json",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Minimum number of bytes needed to validate most signatures.
    /// </summary>
    public const int MinBytesForValidation = 16;

    /// <summary>
    /// Validates that the file header bytes match the expected signatures for the claimed content type.
    /// </summary>
    /// <param name="headerBytes">First N bytes of the file (at least <see cref="MinBytesForValidation"/>).</param>
    /// <param name="claimedContentType">The MIME type claimed by the client.</param>
    /// <returns>
    /// True if the file signature matches the claimed type, or if the type is text-based/unknown.
    /// False if there's a signature mismatch indicating potential spoofing.
    /// </returns>
    public static bool Validate(ReadOnlySpan<byte> headerBytes, string? claimedContentType)
    {
        if (string.IsNullOrWhiteSpace(claimedContentType))
            return false;

        // Text-based types can't be validated via magic bytes - allow them
        if (TextBasedTypes.Contains(claimedContentType))
            return true;

        // Check exact match first
        if (KnownSignatures.TryGetValue(claimedContentType, out var signatures))
            return MatchesAnySignature(headerBytes, signatures);

        // For types with no known signature, allow (fail-open for extensibility)
        // This allows new formats to be added to AllowedUploadTypes without
        // requiring immediate signature support
        return true;
    }

    /// <summary>
    /// Checks if the header bytes match any of the provided signatures.
    /// </summary>
    private static bool MatchesAnySignature(ReadOnlySpan<byte> headerBytes, Signature[] signatures)
    {
        foreach (var sig in signatures)
        {
            if (headerBytes.Length < sig.Offset + sig.Bytes.Length)
                continue;

            var slice = headerBytes.Slice(sig.Offset, sig.Bytes.Length);
            if (slice.SequenceEqual(sig.Bytes))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Asynchronously reads the header bytes from a stream for validation.
    /// Resets the stream position to the beginning after reading.
    /// </summary>
    public static async Task<byte[]> ReadHeaderAsync(Stream stream, CancellationToken ct = default)
    {
        var buffer = new byte[MinBytesForValidation];
        var originalPosition = stream.Position;

        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) break;
            totalRead += read;
        }

        // Reset stream position for subsequent operations
        if (stream.CanSeek)
            stream.Position = originalPosition;

        return buffer[..totalRead];
    }

    /// <summary>
    /// Validates a stream's content against the claimed content type.
    /// Reads header bytes and validates signature, then resets stream position.
    /// </summary>
    public static async Task<bool> ValidateStreamAsync(
        Stream stream, string? claimedContentType, CancellationToken ct = default)
    {
        var headerBytes = await ReadHeaderAsync(stream, ct);
        return Validate(headerBytes, claimedContentType);
    }
}
