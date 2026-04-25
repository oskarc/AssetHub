namespace AssetHub.Domain.Entities;

/// <summary>
/// External-reviewer invitation (T4-GUEST-01). Admin invites by email;
/// the invitee clicks a magic link that auto-provisions a Keycloak guest
/// user and grants viewer ACL on the named collections. The link is a
/// Data-Protection-signed token; the SHA-256 hash of the plaintext is
/// stored here for redemption lookup.
///
/// One-time use: <see cref="AcceptedAt"/> stamps when redeemed; subsequent
/// hits to the accept endpoint with the same token are rejected.
/// </summary>
public class GuestInvitation
{
    public Guid Id { get; set; }

    /// <summary>Lower-cased email address — case-insensitive on uniqueness checks.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>SHA-256 of the plaintext magic-link token, used for lookup at redemption.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Collection ids the guest gets viewer ACL on after accepting.</summary>
    public List<Guid> CollectionIds { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Hard expiry: the invitation can't be redeemed after this even if
    /// the link hasn't been clicked. The background expiry sweep also uses
    /// this to revoke ACLs once an accepted guest's window closes.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Stamped on first redemption. Null = still pending.</summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>Keycloak user id of the provisioned guest user. Set on accept.</summary>
    public string? AcceptedUserId { get; set; }

    /// <summary>Admin who minted the invitation.</summary>
    public string CreatedByUserId { get; set; } = string.Empty;

    /// <summary>Stamped when an admin or the expiry sweep revokes the access.</summary>
    public DateTime? RevokedAt { get; set; }
}
