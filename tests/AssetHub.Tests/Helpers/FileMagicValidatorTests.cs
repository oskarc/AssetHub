using AssetHub.Application.Helpers;

namespace AssetHub.Tests.Helpers;

/// <summary>
/// Unit tests for FileMagicValidator — verifies file signature validation
/// to prevent Content-Type spoofing attacks.
/// </summary>
public class FileMagicValidatorTests
{
    // ── JPEG ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidJpeg_ReturnsTrue()
    {
        // JPEG magic bytes: FF D8 FF
        var header = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01 };
        
        var result = FileMagicValidator.Validate(header, "image/jpeg");
        
        Assert.True(result);
    }

    [Fact]
    public void Validate_InvalidJpeg_ReturnsFalse()
    {
        // PNG header claiming to be JPEG
        var header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };
        
        var result = FileMagicValidator.Validate(header, "image/jpeg");
        
        Assert.False(result);
    }

    // ── PNG ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidPng_ReturnsTrue()
    {
        // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
        var header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };
        
        var result = FileMagicValidator.Validate(header, "image/png");
        
        Assert.True(result);
    }

    [Fact]
    public void Validate_JpegClaimingToBePng_ReturnsFalse()
    {
        var header = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01 };
        
        var result = FileMagicValidator.Validate(header, "image/png");
        
        Assert.False(result);
    }

    // ── GIF ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x3B, 0x00, 0x00 })] // GIF87a
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x3B, 0x00, 0x00 })] // GIF89a
    public void Validate_ValidGif_ReturnsTrue(byte[] header)
    {
        var result = FileMagicValidator.Validate(header, "image/gif");
        
        Assert.True(result);
    }

    // ── PDF ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidPdf_ReturnsTrue()
    {
        // PDF magic bytes: %PDF (25 50 44 46)
        var header = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A, 0x0A };
        
        var result = FileMagicValidator.Validate(header, "application/pdf");
        
        Assert.True(result);
    }

    [Fact]
    public void Validate_ExecutableClaimingToBePdf_ReturnsFalse()
    {
        // Windows executable (MZ header)
        var header = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 };
        
        var result = FileMagicValidator.Validate(header, "application/pdf");
        
        Assert.False(result);
    }

    // ── ZIP / Office documents ──────────────────────────────────────────────

    [Fact]
    public void Validate_ValidZip_ReturnsTrue()
    {
        // ZIP magic bytes: PK (50 4B 03 04)
        var header = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        
        var result = FileMagicValidator.Validate(header, "application/zip");
        
        Assert.True(result);
    }

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    public void Validate_OoxmlFormats_AreZipBased(string mimeType)
    {
        // OOXML formats are ZIP files
        var header = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        
        var result = FileMagicValidator.Validate(header, mimeType);
        
        Assert.True(result);
    }

    // ── MP3 ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })] // MP3 frame sync
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })] // ID3 tag
    public void Validate_ValidMp3_ReturnsTrue(byte[] header)
    {
        var result = FileMagicValidator.Validate(header, "audio/mpeg");
        
        Assert.True(result);
    }

    // ── WebP ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidWebP_ReturnsTrue()
    {
        // WebP: RIFF header (52 49 46 46)
        var header = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20 };
        
        var result = FileMagicValidator.Validate(header, "image/webp");
        
        Assert.True(result);
    }

    // ── Text-based types (no magic bytes) ───────────────────────────────────

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/csv")]
    [InlineData("application/json")]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("application/svg+xml")]
    public void Validate_TextBasedTypes_AlwaysReturnsTrue(string mimeType)
    {
        // Any content should pass for text-based types
        var header = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };
        
        var result = FileMagicValidator.Validate(header, mimeType);
        
        Assert.True(result);
    }

    // ── Unknown types ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_UnknownMimeType_ReturnsTrue()
    {
        // Types without known signatures should pass (fail-open for extensibility)
        var header = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };
        
        var result = FileMagicValidator.Validate(header, "application/x-custom-unknown-type");
        
        Assert.True(result);
    }

    // ── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NullContentType_ReturnsFalse()
    {
        var header = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01 };
        
        var result = FileMagicValidator.Validate(header, null);
        
        Assert.False(result);
    }

    [Fact]
    public void Validate_EmptyContentType_ReturnsFalse()
    {
        var header = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01 };
        
        var result = FileMagicValidator.Validate(header, "");
        
        Assert.False(result);
    }

    [Fact]
    public void Validate_HeaderTooShort_ReturnsFalse()
    {
        // PNG requires 8 bytes, but only 4 provided
        var header = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        
        var result = FileMagicValidator.Validate(header, "image/png");
        
        Assert.False(result);
    }

    [Fact]
    public void Validate_EmptyHeader_ReturnsFalse()
    {
        var header = Array.Empty<byte>();
        
        var result = FileMagicValidator.Validate(header, "image/jpeg");
        
        Assert.False(result);
    }

    // ── Stream validation ───────────────────────────────────────────────────

    [Fact]
    public async Task ValidateStreamAsync_ValidJpeg_ReturnsTrue()
    {
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01 };
        using var stream = new MemoryStream(jpegBytes);
        
        var result = await FileMagicValidator.ValidateStreamAsync(stream, "image/jpeg");
        
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateStreamAsync_ResetsStreamPosition()
    {
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01 };
        using var stream = new MemoryStream(jpegBytes);
        stream.Position = 5; // Start at non-zero position
        
        await FileMagicValidator.ValidateStreamAsync(stream, "image/jpeg");
        
        Assert.Equal(5, stream.Position); // Should be reset to original position
    }

    // ── Spoofing attack scenarios ───────────────────────────────────────────

    [Fact]
    public void Validate_ExecutableClaimingToBeJpeg_ReturnsFalse()
    {
        // Windows PE executable with .jpg extension and image/jpeg Content-Type
        var header = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 };
        
        var result = FileMagicValidator.Validate(header, "image/jpeg");
        
        Assert.False(result);
    }

    [Fact]
    public void Validate_ShellScriptClaimingToBeJpeg_ReturnsFalse()
    {
        // Shell script header
        var header = new byte[] { 0x23, 0x21, 0x2F, 0x62, 0x69, 0x6E, 0x2F, 0x62, 0x61, 0x73, 0x68, 0x0A, 0x72, 0x6D, 0x20, 0x2D };
        
        var result = FileMagicValidator.Validate(header, "image/jpeg");
        
        Assert.False(result);
    }

    [Fact]
    public void Validate_HtmlClaimingToBePng_ReturnsFalse()
    {
        // HTML file claiming to be PNG
        var header = new byte[] { 0x3C, 0x21, 0x44, 0x4F, 0x43, 0x54, 0x59, 0x50, 0x45, 0x20, 0x68, 0x74, 0x6D, 0x6C, 0x3E, 0x0A };
        
        var result = FileMagicValidator.Validate(header, "image/png");
        
        Assert.False(result);
    }
}
