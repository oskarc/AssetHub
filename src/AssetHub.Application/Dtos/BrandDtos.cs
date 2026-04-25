using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

public class CreateBrandDto
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>CSS hex literal: <c>#RGB</c> or <c>#RRGGBB</c> (case-insensitive).</summary>
    [Required, RegularExpression(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$",
        ErrorMessage = "Must be a CSS hex literal like #1976D2 or #ABC.")]
    public string PrimaryColor { get; set; } = "#1976D2";

    [Required, RegularExpression(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$",
        ErrorMessage = "Must be a CSS hex literal like #424242.")]
    public string SecondaryColor { get; set; } = "#424242";

    public bool IsDefault { get; set; }
}

public class UpdateBrandDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; set; }

    [RegularExpression(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$")]
    public string? PrimaryColor { get; set; }

    [RegularExpression(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$")]
    public string? SecondaryColor { get; set; }

    public bool? IsDefault { get; set; }
}

public class BrandResponseDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required bool IsDefault { get; set; }
    public string? LogoObjectKey { get; set; }

    /// <summary>
    /// Presigned URL for the logo (5-minute expiry). Surfaced in the
    /// admin list and the public share-info response so the client
    /// doesn't need a separate fetch. Null when no logo is uploaded.
    /// </summary>
    public string? LogoUrl { get; set; }

    public required string PrimaryColor { get; set; }
    public required string SecondaryColor { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string CreatedByUserId { get; set; }
    public required DateTime UpdatedAt { get; set; }
}
