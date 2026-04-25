namespace AssetHub.Application.Configuration;

/// <summary>
/// On-the-fly rendition policy (T3-REND-01). Allowlists the dimensions /
/// formats / fit modes the public render endpoint will accept; anything
/// outside is 400 Bad Request. The strict allowlist is the primary
/// defence against DoS-by-massive-resize.
/// </summary>
public class RenditionSettings
{
    public const string SectionName = "Renditions";

    /// <summary>Allowed widths in pixels. Defaults cover the typical responsive breakpoints.</summary>
    public List<int> AllowedWidths { get; set; } = new() { 100, 200, 400, 800, 1200, 1600, 2400 };

    /// <summary>Allowed heights in pixels. Same defaults as widths so admins can mix and match.</summary>
    public List<int> AllowedHeights { get; set; } = new() { 100, 200, 400, 800, 1200, 1600, 2400 };

    /// <summary>Output format tokens (lowercase). "jpeg", "png", "webp".</summary>
    public List<string> AllowedFormats { get; set; } = new() { "jpeg", "png", "webp" };

    /// <summary>Fit-mode tokens. "cover", "contain".</summary>
    public List<string> AllowedFitModes { get; set; } = new() { "cover", "contain" };

    /// <summary>Quality (1–100) used when re-encoding to JPEG / WebP.</summary>
    public int Quality { get; set; } = 85;

    /// <summary>
    /// Presigned-URL expiry returned by the render endpoint. 1 hour balances
    /// CDN cacheability with the periodic key rotation Data Protection
    /// performs under the hood.
    /// </summary>
    public int PresignedUrlExpirySeconds { get; set; } = 3600;
}
