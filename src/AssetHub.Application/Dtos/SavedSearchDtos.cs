using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

// ── Response ────────────────────────────────────────────────────────────

public class SavedSearchDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required string OwnerUserId { get; set; }
    public required AssetSearchRequest Request { get; set; }
    public required string Notify { get; set; }
    public DateTime? LastRunAt { get; set; }
    public required DateTime CreatedAt { get; set; }
}

// ── Create ──────────────────────────────────────────────────────────────

public class CreateSavedSearchDto
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Name { get; set; }

    [Required]
    public required AssetSearchRequest Request { get; set; }

    /// <summary>"none" | "on_new_match" | "daily" | "weekly" — v1 only persists the preference.</summary>
    [Required]
    [StringLength(50)]
    public required string Notify { get; set; }
}

// ── Update ──────────────────────────────────────────────────────────────

public class UpdateSavedSearchDto
{
    [StringLength(255, MinimumLength = 1)]
    public string? Name { get; set; }

    public AssetSearchRequest? Request { get; set; }

    [StringLength(50)]
    public string? Notify { get; set; }
}
