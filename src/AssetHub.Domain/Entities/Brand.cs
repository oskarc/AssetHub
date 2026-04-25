namespace AssetHub.Domain.Entities;

/// <summary>
/// Branding theme applied to public share pages (T4-BP-01). Each
/// <see cref="Collection"/> can opt into a brand via
/// <see cref="Collection.BrandId"/>; one brand can additionally be marked
/// <see cref="IsDefault"/> = true (only one row at a time, enforced at
/// the service layer) and is used when a share's owning collection has no
/// brand assigned.
///
/// Custom CSS and custom-domain features from the roadmap spec are
/// deferred — see FOLLOW-UPS. v1 ships logo + primary/secondary colours
/// only; the share page reads those into CSS variables.
/// </summary>
public class Brand
{
    public Guid Id { get; set; }

    /// <summary>Admin-friendly display name ("Acme Brand", "EU subsidiary").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Exactly one brand at a time can be the default; the brand-resolution
    /// path falls back to it when a share's collection has no
    /// <c>BrandId</c>. Service layer enforces single-default invariant.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// MinIO object key for the logo (PNG / SVG / WebP). Null = no logo,
    /// share page renders just the wordmark / colours.
    /// </summary>
    public string? LogoObjectKey { get; set; }

    /// <summary>
    /// Primary brand colour as a CSS hex literal (<c>#RRGGBB</c>). Bound
    /// to <c>--mud-palette-primary</c> on the share layout.
    /// </summary>
    public string PrimaryColor { get; set; } = "#1976D2";

    /// <summary>Secondary / accent colour, hex literal.</summary>
    public string SecondaryColor { get; set; } = "#424242";

    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
