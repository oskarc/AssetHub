using System.ComponentModel.DataAnnotations;
using AssetHub.Application.Validation;

namespace AssetHub.Application.Dtos;

public class CreateGuestInvitationDto
{
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Collection ids the guest gets viewer ACL on after accepting.</summary>
    [Required, MinLength(1), MaxItems(50)]
    public List<Guid> CollectionIds { get; set; } = new();

    /// <summary>
    /// Days until the invitation expires (and the guest's access is
    /// revoked even if they redeemed the link). 1–90 inclusive.
    /// </summary>
    [Range(1, 90)]
    public int ExpiresInDays { get; set; } = 14;
}

public class GuestInvitationResponseDto
{
    public required Guid Id { get; set; }
    public required string Email { get; set; }
    public required List<Guid> CollectionIds { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? AcceptedUserId { get; set; }
    public required string CreatedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Computed status: <c>pending</c>, <c>accepted</c>, <c>expired</c>,
    /// <c>revoked</c>. Surfaces directly in the admin UI.
    /// </summary>
    public required string Status { get; set; }
}

/// <summary>
/// Returned once at creation. The plaintext magic-link token is
/// embedded in the magic-link URL emailed to the guest. After this
/// response the only way to redeem is the email link.
/// </summary>
public class CreatedGuestInvitationDto
{
    public required GuestInvitationResponseDto Invitation { get; set; }
    public required string MagicLinkUrl { get; set; }
}

public class AcceptGuestInvitationResponseDto
{
    public required Guid InvitationId { get; set; }
    public required string Email { get; set; }
    public required List<Guid> CollectionIds { get; set; }
    public required DateTime ExpiresAt { get; set; }
}
